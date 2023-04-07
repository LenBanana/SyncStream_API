using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task ChangeDownload(string token, int fileId, string name)
        {
            if (name == null || name.Length <= 2)
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "Filename has to be at least 3 characters long", Answer1 = "Ok" });
                return;
            }
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
            if (dbUser == null)
                return;
            if (dbUser.userprivileges >= UserPrivileges.Administrator)
            {
                var result = _postgres.Users.Include(x => x.Files).FirstOrDefault(x => x.ID == dbUser.ID)?.Files.FirstOrDefault(x => x.ID == fileId);
                result.Name = name;
                await _postgres.SaveChangesAsync();
                if (result.DbFileFolderId > 0)
                    await GetFolderFiles(token, result.DbFileFolderId);
            }
        }

        public async Task GetDownloads(string token)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Administrator)
                {
                    var userFiles = _postgres.Files?.Where(x => x.DbUserID == dbUser.ID).ToList();
                    var result = userFiles.Select(x => new FileDto(x)).OrderBy(x => x.Name).ToList();
                    await Clients.Caller.getDownloads(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in 'GetDownloads'");
                Console.WriteLine(ex.ToString());
            }
        }

        public async Task GetAllDownloads(string token)
        {
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
            if (dbUser == null)
                return;
            if (dbUser.userprivileges >= UserPrivileges.Elevated)
            {
                var result = _postgres.Files?.Select(x => new FileDto(x)).OrderBy(x => x.Name).ToList();
                await Clients.Caller.getDownloads(result);
            }
        }

        public async Task ReloadDownloadConfig(string token)
        {
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
            if (dbUser == null)
                return;
            if (dbUser.userprivileges >= UserPrivileges.Elevated)
            {
                GeneralManager.ReadSettings(Configuration);
                await Clients.Caller.dialog(new Dialog(AlertTypes.Info) { Header = "Reload configuration", Question = "Reload successful", Answer1 = "Ok" });
            }
        }

        public async Task GetFolders(string token, int folderId = 1)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Administrator)
                {
                    var shareFolders = _postgres.FolderShare?.Where(x => x.DbUserID == dbUser.ID);
                    var folder = _postgres.Folders?.Where(x => x.DbUserID == null || x.DbUserID == dbUser.ID || shareFolders.FirstOrDefault(y => y.DbFolderID == x.Id) != null || shareFolders.FirstOrDefault(y => y.DbFolderID == x.ParentId) != null).OrderBy(x => x.Name).ToList();
                    var defaultFolder = folder.FirstOrDefault(x => x.Id == folderId);
                    if (defaultFolder != null)
                    {
                        var folderResult = new FolderDto(defaultFolder);
                        await Clients.Group(token).getFolders(folderResult);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in 'GetFolders'");
                Console.WriteLine(ex.ToString());
            }
        }

        public async Task AddFolder(string token, int folderId)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Administrator)
                {
                    var parent = _postgres.Folders?.FirstOrDefault(x => x.Id == folderId);
                    if (parent == null)
                        throw new Exception("Could not find folder");
                    if (parent.DbUserID != null && parent.DbUserID != dbUser.ID)
                        throw new Exception("You do not own this folder");
                    var newFolder = new DbFileFolder("New Folder", parent.Id, dbUser.ID);
                    _postgres.Folders.Add(newFolder);
                    await _postgres.SaveChangesAsync();
                    await GetFolders(token, folderId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in 'AddFolder'");
                await Clients.Caller.dialog(new Dialog(AlertTypes.Warning) { Header = "Error", Question = ex?.Message, Answer1 = "Ok" });
                Console.WriteLine(ex.ToString());
            }
        }

        public async Task DeleteFolder(string token, int folderId)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Administrator)
                {
                    var folder = _postgres.Folders?.Include(x => x.Files).FirstOrDefault(x => x.Id == folderId && (x.DbUserID == dbUser.ID || dbUser.userprivileges > UserPrivileges.Administrator));
                    if (folder != null)
                    {
                        if (folder.Files?.Count > 0 || _postgres.Folders?.Where(x => x.ParentId == folderId).Count() > 0)
                        {
                            await Clients.Caller.dialog(new Dialog(AlertTypes.Warning) { Header = "Folder deletion", Question = "Prevented folder deletion because it still contains files", Answer1 = "Ok" });
                            return;
                        }
                        _postgres.Folders.Remove(folder);
                        await _postgres.SaveChangesAsync();
                        await GetFolders(token, (int)(folder.ParentId.HasValue ? folder.ParentId : 1));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in 'DeleteFolder'");
                await Clients.Caller.dialog(new Dialog(AlertTypes.Warning) { Header = "Error", Question = ex?.Message, Answer1 = "Ok" });
                Console.WriteLine(ex.ToString());
            }
        }

        public async Task DownloadFile(string token, string url, string fileName, ConversionPreset preset = ConversionPreset.SuperFast)
        {
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
            if (dbUser == null)
                return;
            if (dbUser.userprivileges >= UserPrivileges.Administrator)
            {
                _manager.AddDownload(new(dbUser.ID, fileName, token, url, preset));
            }
        }

        public async void DownloadYtVideo(string token, string url, string quality = "1080", bool audioOnly = false)
        {
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
            if (dbUser == null)
                return;
            if (dbUser.userprivileges >= UserPrivileges.Administrator)
            {

                if (url.Contains("playlist?list="))
                {
                    var ytdl = General.GetYoutubeDL();
                    var playlistInfo = await ytdl.RunVideoDataFetch(url);
                    var vids = playlistInfo.Data.Entries.Select(x => new DownloadClientValue(dbUser.ID, x.Title, token, x.Url, quality)).ToList();
                    foreach (var vid in vids)
                    {
                        _manager.userM3U8Conversions.Add(vid);
                        await _manager.YtDownload(vid, audioOnly);
                        if (vid.CancellationToken.IsCancellationRequested)
                            break;
                        _manager.userM3U8Conversions.Remove(vid);
                    }
                    return;
                }
                var fileName = await General.ResolveURL(url, Configuration);
                var conv = new DownloadClientValue(dbUser.ID, fileName, token, url, quality);
                _manager.userM3U8Conversions.Add(conv);
                await _manager.YtDownload(conv, audioOnly);
                _manager.userM3U8Conversions.Remove(conv);
            }
        }

        public async Task GetFileInfo(string token, int id)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges < UserPrivileges.Administrator)
                    throw new Exception("Permission denied");
                var file = _postgres.Files.ToList().FirstOrDefault(x => x.ID == id);
                if (file == null)
                    throw new Exception("File not found in database");
                var path = $"{(file.Temporary ? General.TemporaryFilePath : General.FilePath)}/{file.FileKey}{file.FileEnding}";
                if (!File.Exists(path))
                    throw new Exception($"File {path} not found");
                var mediaInfo = await Xabe.FFmpeg.FFmpeg.GetMediaInfo(path);
                if (mediaInfo == null)
                    throw new Exception("Media info was null");
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
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                await Clients.Caller.dialog(new Dialog(AlertTypes.Warning) { Header = "Error", Question = ex?.Message, Answer1 = "Ok" });
            }
        }

        public async Task ShareFolder(string token, int folderId, int userId)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    throw new Exception("Requesting user not found");
                if (dbUser.ID == folderId)
                    throw new Exception("Can't share folders with yourself");
                if (dbUser.userprivileges < UserPrivileges.Administrator)
                    throw new Exception("Permission denied");
                var shareUser = _postgres.Users?.FirstOrDefault(x => x.ID == userId);
                if (shareUser == null)
                    throw new Exception("Share user does not exist");
                var shareFolder = _postgres.Folders?.FirstOrDefault(x => x.Id == folderId);
                if (shareFolder == null)
                    throw new Exception("Share folder does not exist");
                if (shareFolder.DbUserID != dbUser.ID)
                    throw new Exception($"Can't share folder {shareFolder.Name} because you don't have ownership");
                var oldFolderShare = _postgres.FolderShare?.FirstOrDefault(x => x.DbFolderID == shareFolder.Id && x.DbUserID == shareUser.ID);
                if (oldFolderShare != null)
                {
                    await Clients.Caller.dialog(new Dialog(AlertTypes.Warning) { Header = "Share", Question = $"Not sharing {shareFolder.Name} with {shareUser.username} anymore!", Answer1 = "Ok" });
                    _postgres.FolderShare?.Remove(oldFolderShare);
                    await _postgres.SaveChangesAsync();
                    return;
                }
                var newShare = new DbFolderUserShare(shareUser.ID, shareFolder.Id);
                _postgres.FolderShare?.Add(newShare);
                await _postgres.SaveChangesAsync();
                await Clients.Caller.dialog(new Dialog(AlertTypes.Success) { Header = "Share", Question = $"Now sharing {shareFolder.Name} with {shareUser.username}!", Answer1 = "Ok" });
                await GetFolders(shareUser.RememberTokens.OrderByDescending(x => x.Created).FirstOrDefault()?.Token, shareFolder.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in 'ShareFolder'");
                Console.WriteLine(ex.ToString());
                await Clients.Caller.dialog(new Dialog(AlertTypes.Warning) { Header = "Error", Question = ex?.Message, Answer1 = "Ok" });
            }
        }

        public async Task GetFolderFiles(string token, int folderId)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                var dbFolder = _postgres.Folders?.Where(x => x.Id == folderId).FirstOrDefault();
                if (dbFolder == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Administrator)
                {
                    var shareFolders = _postgres.FolderShare?.Where(x => x.DbUserID == dbUser.ID);
                    var files = _postgres.Files?.Where(x => x.DbFileFolderId == folderId && (x.DbUserID == dbUser.ID || shareFolders.FirstOrDefault(y => y.DbFolderID == folderId) != null || shareFolders.FirstOrDefault(y => y.DbFolderID == dbFolder.ParentId) != null || dbFolder.DbUserID == dbUser.ID));
                    if (files != null)
                        await Clients.Caller.getFolderFiles(files.Select(x => new FileDto(x)).ToList());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in 'GetFolderFiles'");
                Console.WriteLine(ex.ToString());
            }
        }

        public async Task ChangeFolderName(string token, int folderId, string folderName)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Administrator)
                {
                    var folder = _postgres.Folders?.FirstOrDefault(x => x.Id == folderId);
                    if (folder != null)
                    {
                        folder.Name = folderName;
                        await _postgres.SaveChangesAsync();
                        await GetFolders(token, (int)folder.ParentId);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in 'ChangeFolderName'");
                Console.WriteLine(ex.ToString());
            }
        }

        public async Task ChangeFolder(string token, int fileId, int folderId)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Administrator)
                {
                    var file = _postgres.Files?.FirstOrDefault(x => x.ID == fileId);
                    if (file != null && file.DbFileFolderId != folderId)
                    {
                        file.DbFileFolderId = folderId;
                        await _postgres.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in 'ChangeFolder'");
                Console.WriteLine(ex.ToString());
            }
        }

        public async Task CleanUpFiles(string token, bool delete = false)
        {
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
            if (dbUser == null)
                return;
            if (dbUser.userprivileges >= UserPrivileges.Elevated)
            {
                List<string> files = new List<string>();
                if (Directory.Exists(General.FilePath))
                    files = Directory.GetFiles(General.FilePath).ToList();
                if (Directory.Exists(General.TemporaryFilePath))
                    files.AddRange(Directory.GetFiles(General.TemporaryFilePath));
                if (files.Count() > 0)
                {
                    var dbFiles = _postgres.Files.ToList();
                    if (dbFiles != null)
                    {
                        var result = files.Where(f => dbFiles.FindIndex(df => (df.FileKey + df.FileEnding).ToLower() == new FileInfo(f).Name.ToLower()) == -1);
                        var text = $"{result?.Count()} Files\n";
                        foreach (var file in result)
                        {
                            if (File.Exists(file))
                                if (delete)
                                    File.Delete(file);
                                else
                                    text += "\n" + $"{file} - {new FileInfo(file).Name}";
                        }
                        if (delete)
                            await Clients.Caller.dialog(new Dialog(AlertTypes.Info) { Header = "Clean up", Question = $"Removed {result?.Count()} files", Answer1 = "Ok" });
                        else
                            await Clients.Caller.dialog(new Dialog(AlertTypes.Info) { Header = "Clean up", Question = text, Answer1 = "Ok" });
                    }
                }
            }
            else
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "You don't have permissions to clean up the video files", Answer1 = "Ok" });
        }

        public async Task CancelConversion(string token, string downloadId)
        {
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
            if (dbUser == null)
                return;
            if (dbUser.userprivileges >= UserPrivileges.Administrator)
            {
                _manager.CancelM3U8Conversion(downloadId);
            }
        }

        public async Task RemoveFile(string token, int id)
        {
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
            if (dbUser == null)
                return;
            if (dbUser.userprivileges >= UserPrivileges.Administrator)
            {
                var file = _postgres.Files.ToList().FirstOrDefault(x => x.ID == id);
                if (file != null)
                {
                    var path = $"{(file.Temporary ? General.TemporaryFilePath : General.FilePath)}/{file.FileKey}{file.FileEnding}";
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                    _postgres.Files.Remove(file);
                    await _postgres.SaveChangesAsync();
                    await Clients.Caller.downloadRemoved(id.ToString());
                    if (file.DbFileFolderId > 0)
                        await GetFolderFiles(token, file.DbFileFolderId);

                }
            }
        }
    }
}
