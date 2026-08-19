using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData.Helper;
using Xabe.FFmpeg;
using YoutubeDLSharp.Options;
using SyncStreamAPI.Wire;
using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.Auth;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    [Authorize(Policy = AuthorizationPolicies.Elevated)]
    public async Task ReloadDownloadConfig()
    {
        GeneralManager.ReadSettings(Configuration);
        await Clients.Caller.dialog(new Dialog
            { Header = "Reload configuration", Question = "Reload successful", Answer1 = "Ok" });
    }

    [Authorize(Policy = AuthorizationPolicies.Elevated)]
    public async Task GetAllDownloads()
    {
        var result = _postgres.Files?.Select(x => new FileDto(x)).OrderBy(x => x.Name).ToList();
        await Clients.Caller.getDownloads(result);
    }

    [Authorize(Policy = AuthorizationPolicies.Elevated)]
    public async Task GetStorageInfo()
    {
        var folders = new List<string> { General.FilePath, General.TemporaryFilePath };
        var storageInfo = folders.Select(f => new FileStorageInfo(f)).ToList();
        storageInfo.ForEach(f => f.CalculateStorageInfo());
        await Clients.Caller.getStorageInfo(storageInfo);
    }

    [Authorize(Policy = AuthorizationPolicies.Elevated)]
    public async Task CleanUpFiles(bool delete = false)
    {
        var files = GetDefaultFiles();
        if (files.Count() > 0)
        {
            var dbFiles = _postgres.Files.ToList();
            if (dbFiles != null)
            {
                var result = files.Where(f =>
                    dbFiles.FindIndex(
                        df => (df.FileKey + df.FileEnding).ToLower() == new FileInfo(f).Name.ToLower()) == -1);
                var text = $"{result?.Count()} Files\n";
                foreach (var file in result)
                    if (File.Exists(file))
                    {
                        if (delete)
                            File.Delete(file);
                        else
                            text += "\n" + $"{file} - {new FileInfo(file).Name}";
                    }

                if (delete)
                    await Clients.Caller.dialog(new Dialog
                        { Header = "Clean up", Question = $"Removed {result?.Count()} files", Answer1 = "Ok" });
                else
                    await Clients.Caller.dialog(new Dialog { Header = "Clean up", Question = text, Answer1 = "Ok" });
            }
        }
    }

    private List<string> GetDefaultFiles()
    {
        var files = new List<string>();
        if (Directory.Exists(General.FilePath)) files = Directory.GetFiles(General.FilePath).ToList();

        if (Directory.Exists(General.TemporaryFilePath)) files.AddRange(Directory.GetFiles(General.TemporaryFilePath));

        return files;
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task ChangeDownload(int fileId, string name)
    {
        if (name is not { Length: > 2 })
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Filename has to be at least 3 characters long", Answer1 = "Ok" });
            return;
        }

        var dbUser = _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefault(x => x.ID == CallerUserId);
        var result = _postgres.Users.Include(x => x.Files).FirstOrDefault(x => x.ID == dbUser.ID)?.Files
            .FirstOrDefault(x => x.ID == fileId);
        result.Name = name;
        await _postgres.SaveChangesAsync();
        if (result.DbFileFolderId > 0) await GetFolderFiles(result.DbFileFolderId);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task GetDownloads()
    {
        var dbUser = _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefault(x => x.ID == CallerUserId);
        var userFiles = _postgres.Files?.Where(x => x.DbUserID == dbUser.ID).ToList();
        var result = userFiles.Select(x => new FileDto(x)).OrderBy(x => x.Name).ToList();
        await Clients.Caller.getDownloads(result);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public Task GetFolders(int folderId = 1)
    {
        return SendFoldersToUser(CallerUserId, folderId);
    }

    /// <summary>
    ///     Sends the folder tree as a given account sees it, to that account's connections.
    /// </summary>
    /// <remarks>
    ///     Split out from <see cref="GetFolders" /> so sharing a folder can notify the recipient rather than
    ///     the person doing the sharing.
    /// </remarks>
    private async Task SendFoldersToUser(int? userId, int folderId)
    {
        var dbUser = _postgres.Users.FirstOrDefault(x => x.ID == userId);
        if (dbUser == null) return;

        var sharedFolderIds = _postgres.FolderShare
            .Where(x => x.DbUserID == dbUser.ID)
            .Select(x => x.DbFolderID)
            .ToList();

        // Fetch all folders that are directly or indirectly shared
        var allSharedFolders = GetAllSharedFolders(sharedFolderIds);

        var folders = _postgres.Folders
            .Where(x => x.DbUserID == null || x.DbUserID == dbUser.ID || allSharedFolders.Contains(x.Id))
            .OrderBy(x => x.Name)
            .ToList();

        var resultFolder = folders.FirstOrDefault(x => x.Id == folderId);
        if (resultFolder != null)
        {
            var folderResult = new FolderDto(resultFolder);
            await Clients.Group(dbUser.ID.ToString()).getFolders(folderResult);
        }
    }

    private List<int> GetAllSharedFolders(List<int> sharedFolderIds)
    {
        var allSharedFolders = new List<int>();
        var nextFolders = sharedFolderIds;
        while (nextFolders.Any())
        {
            allSharedFolders.AddRange(nextFolders);
            nextFolders = _postgres.Folders
                .Where(x => nextFolders.Contains(x.ParentId.Value))
                .Select(x => x.Id)
                .ToList();
        }

        return allSharedFolders.Distinct().ToList();
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task AddFolder(int folderId)
    {
        var dbUser = _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefault(x => x.ID == CallerUserId);
        var parent = _postgres.Folders?.FirstOrDefault(x => x.Id == folderId);
        if (parent == null)
        {
            await _manager.SendDefaultDialog(dbUser.ID.ToString(), "Could not find folder", AlertType.Warning);
            return;
        }

        if (parent.DbUserID != null && parent.DbUserID != dbUser.ID)
        {
            await _manager.SendDefaultDialog(dbUser.ID.ToString(), "You do not own this folder", AlertType.Warning);
            return;
        }

        var newFolder = new DbFileFolder("New Folder", parent.Id, dbUser.ID);
        _postgres.Folders.Add(newFolder);
        await _postgres.SaveChangesAsync();
        await GetFolders(folderId);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task DeleteFolder(int folderId)
    {
        var dbUser = _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefault(x => x.ID == CallerUserId);
        var folder = _postgres.Folders?.Include(x => x.Files).FirstOrDefault(x =>
            x.Id == folderId && (x.DbUserID == dbUser.ID || dbUser.userprivileges > UserPrivileges.Administrator));
        if (folder != null)
        {
            if (folder.Files?.Count > 0 || _postgres.Folders?.Where(x => x.ParentId == folderId).Count() > 0)
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Warning)
                {
                    Header = "Folder deletion",
                    Question = "Prevented folder deletion because it still contains files", Answer1 = "Ok"
                });
                return;
            }

            _postgres.Folders.Remove(folder);
            await _postgres.SaveChangesAsync();
            await GetFolders((int)(folder.ParentId.HasValue ? folder.ParentId : 1));
        }
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task DownloadFile(string url, string fileName,
        ConversionPreset preset = ConversionPreset.SuperFast)
    {
        var dbUser = await _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.ID == CallerUserId);
        if (dbUser.userprivileges >= UserPrivileges.Administrator)
            _ = _manager.AddDownload(new DownloadClientValue(dbUser.ID, fileName, url, preset));
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task DownloadYtVideo(string url, string quality = "1080", bool audioOnly = false,
        bool embedSubtitles = false)
    {
        var dbUser = await _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.ID == CallerUserId);
        if (url.Contains("playlist?list="))
        {
            var ytdl = General.GetYoutubeDl();
            var playlistInfo = await ytdl.RunVideoDataFetch(url, overrideOptions: new OptionSet
            {
                ForceIPv4 = true
            });
            var vids = playlistInfo.Data.Entries.Select(x =>
                    new DownloadClientValue(dbUser.ID, x.Title, x.Url, quality, audioOnly, embedSubtitles))
                .ToList();
            _manager.YtPlaylistDownload(vids);
            return;
        }

        var fileName = await General.ResolveUrl(url, Configuration);
        var conv = new DownloadClientValue(dbUser.ID, fileName, url, quality, audioOnly);
        _ = _manager.YtDlpDownload(conv);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task GetFileInfo(int id)
    {
        var dbUser = await _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.ID == CallerUserId);
        var file = _postgres.Files.ToList().FirstOrDefault(x => x.ID == id);
        if (file == null)
        {
            await _manager.SendDefaultDialog(dbUser.ID.ToString(), "File not found in database", AlertType.Warning);
            return;
        }

        var path =
            $"{(file.Temporary ? General.TemporaryFilePath : General.FilePath)}/{file.FileKey}{file.FileEnding}";
        if (!File.Exists(path))
        {
            await _manager.SendDefaultDialog(dbUser.ID.ToString(), $"File {path} not found", AlertType.Warning);
            return;
        }

        IMediaInfo? mediaInfo = null;
        try
        {
            mediaInfo = await FFmpeg.GetMediaInfo(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }

        if (mediaInfo == null)
        {
            await _manager.SendDefaultDialog(dbUser.ID.ToString(), "Could not get media info", AlertType.Warning);
            return;
        }

        var fileInfo = new FileInfo(path);
        var downloadFileInfo = new DownloadFileInfo(
            file.Name,
            file.FileEnding,
            fileInfo.Length,
            Encryption.SHA256CheckSum(path),
            mediaInfo.VideoStreams.FirstOrDefault(),
            mediaInfo.AudioStreams.FirstOrDefault(),
            fileInfo.CreationTimeUtc
        );
        await Clients.Caller.getFileInfo(downloadFileInfo);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task GetFolderUsers(int folderId)
    {
        var dbUser = await _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.ID == CallerUserId);
        var folder = _postgres.Folders?.FirstOrDefault(x => x.Id == folderId);
        if (folder == null)
        {
            await _manager.SendDefaultDialog(dbUser.ID.ToString(), "Folder not found", AlertType.Warning);
            return;
        }

        var folderShares = _postgres.FolderShare?.Where(x => x.DbFolderID == folderId);
        var users = _postgres.Users?.Where(x =>
            x.ID == folder.DbUserID || folderShares.FirstOrDefault(y => y.DbUserID == x.ID) != null).ToList();
        await Clients.Caller.getFolderUsers(users.ToSummaries());
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task ShareFolder(int folderId, int userId)
    {
        var dbUser = await _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.ID == CallerUserId);
        if (dbUser == null)
            // Handle invalid token scenario
            return;

        if (dbUser.ID == folderId)
        {
            await SendWarningDialog(dbUser.ID, "Can't share folders with yourself");
            return;
        }

        var shareUser = await _postgres.Users.FirstOrDefaultAsync(x => x.ID == userId);
        if (shareUser == null)
        {
            await SendWarningDialog(dbUser.ID, "Share user does not exist");
            return;
        }

        var shareFolder = await _postgres.Folders.FirstOrDefaultAsync(x => x.Id == folderId);
        if (shareFolder == null)
        {
            await SendWarningDialog(dbUser.ID, "Share folder does not exist");
            return;
        }

        if (shareFolder.DbUserID != dbUser.ID)
        {
            await SendWarningDialog(dbUser.ID,
                $"Can't share folder {shareFolder.Name} because you don't have ownership");
            return;
        }

        await HandleFolderSharing(dbUser, shareUser, shareFolder);
    }

    private async Task SendWarningDialog(int userId, string message)
    {
        await _manager.SendDefaultDialog(userId.ToString(), message, AlertType.Warning);
    }

    private async Task HandleFolderSharing(DbUser dbUser, DbUser shareUser, DbFileFolder shareFolder)
    {
        var oldFolderShare = await _postgres.FolderShare.FirstOrDefaultAsync(x =>
            x.DbFolderID == shareFolder.Id && x.DbUserID == shareUser.ID);
        if (oldFolderShare != null)
        {
            _postgres.FolderShare.Remove(oldFolderShare);
        }
        else
        {
            var newShare = new DbFolderUserShare(shareUser.ID, shareFolder.Id);
            _postgres.FolderShare.Add(newShare);
        }

        await _postgres.SaveChangesAsync();

        // Push the updated tree to the user the folder was shared with, addressed by their account. This
        // used to reach for that user's newest remember token and send to a group named after it.
        await SendFoldersToUser(shareUser.ID, shareFolder.Id);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task GetFolderFiles(int folderId)
    {
        var dbUser = await _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.ID == CallerUserId);
        if (dbUser == null) return;

        var dbFolder = await _postgres.Folders.FirstOrDefaultAsync(x => x.Id == folderId);
        if (dbFolder == null)
        {
            await SendWarningDialog(dbUser.ID, "Could not find folder");
            return;
        }

        var sharedFolderIds = _postgres.FolderShare
            .Where(x => x.DbUserID == dbUser.ID)
            .Select(x => x.DbFolderID)
            .ToList();

        var allAncestorFolderIds = GetAllAncestorFolders(folderId);
        allAncestorFolderIds.Add(folderId);

        if (dbFolder.DbUserID == dbUser.ID || sharedFolderIds.Intersect(allAncestorFolderIds).Any() ||
            _postgres.Files.Any(x => x.DbFileFolderId == folderId && x.DbUserID == dbUser.ID))
        {
            var files = _postgres.Files
                .Where(x => x.DbFileFolderId == folderId)
                .ToList();
            if (folderId == General.DefaultFolderId) files = files.Where(x => x.DbUserID == dbUser.ID).ToList();

            if (files.Any())
            {
                await Clients.Caller.getFolderFiles(files.Select(x => new FileDto(x)).ToList());
                return;
            }
        }

        await Clients.Caller.getFolderFiles(new List<FileDto>());
    }

    private List<int> GetAllAncestorFolders(int folderId)
    {
        var ancestorFolders = new List<int>();
        int? currentFolderId = folderId;

        while (currentFolderId.HasValue)
        {
            var parentFolder = _postgres.Folders.FirstOrDefault(x => x.Id == currentFolderId.Value);
            if (parentFolder is { ParentId: not null })
            {
                ancestorFolders.Add(parentFolder.ParentId.Value);
                currentFolderId = parentFolder.ParentId;
            }
            else
            {
                currentFolderId = null;
            }
        }

        return ancestorFolders;
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task ChangeFolderName(int folderId, string folderName)
    {
        var folder = _postgres.Folders?.FirstOrDefault(x => x.Id == folderId);
        if (folder != null)
        {
            folder.Name = folderName;
            await _postgres.SaveChangesAsync();
            if (folder.ParentId != null) await GetFolders((int)folder.ParentId);
        }
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task ChangeFolder(int fileId, int folderId)
    {
        var file = _postgres.Files?.FirstOrDefault(x => x.ID == fileId);
        if (file != null && file.DbFileFolderId != folderId)
        {
            file.DbFileFolderId = folderId;
            await _postgres.SaveChangesAsync();
        }
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task CancelConversion(string downloadId)
    {
        var dbUser = await _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.ID == CallerUserId);
        await _manager.CancelDownload(dbUser.ID, downloadId);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task RemoveFile(int id)
    {
        var dbUser = await _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.ID == CallerUserId);
        var file = _postgres.Files.ToList().FirstOrDefault(x => x.ID == id);
        if (file != null && (file.DbUserID == dbUser.ID || dbUser.userprivileges >= UserPrivileges.Elevated))
        {
            var path =
                $"{(file.Temporary ? General.TemporaryFilePath : General.FilePath)}/{file.FileKey}{file.FileEnding}";
            if (File.Exists(path)) File.Delete(path);

            _postgres.Files.Remove(file);
            await _postgres.SaveChangesAsync();
            await Clients.Caller.downloadRemoved(id.ToString());
            if (file.DbFileFolderId > 0) await GetFolderFiles(file.DbFileFolderId);
        }
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task MakeFilePermanent(int id)
    {
        var dbUser = await _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.ID == CallerUserId);
        var file = _postgres.Files.ToList().FirstOrDefault(x => x.ID == id);
        if (file is { Temporary: true } &&
            (file.DbUserID == dbUser.ID || dbUser.userprivileges >= UserPrivileges.Elevated))
        {
            file.DateToBeDeleted = null;
            await _postgres.SaveChangesAsync();
            await GetFolderFiles(file.DbFileFolderId);
        }
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task ToggleFilePublic(int id)
    {
        var dbUser = await _postgres.Users.Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.ID == CallerUserId);
        var file = _postgres.Files.ToList().FirstOrDefault(x => x.ID == id);
        if (file != null && (file.DbUserID == dbUser.ID || dbUser.userprivileges >= UserPrivileges.Elevated))
        {
            file.Public = !file.Public;
            await _postgres.SaveChangesAsync();
            await GetFolderFiles(file.DbFileFolderId);
        }
    }
}