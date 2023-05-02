using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
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

        [Privilege(RequiredPrivileges = UserPrivileges.Elevated, AuthenticationType = AuthenticationType.Token)]
        public async Task GetAllDownloads(string token)
        {
            var result = _postgres.Files?.Select(x => new FileDto(x)).OrderBy(x => x.Name).ToList();
            await Clients.Caller.getDownloads(result);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Elevated, AuthenticationType = AuthenticationType.Token)]
        public async Task ReloadDownloadConfig(string token)
        {
            GeneralManager.ReadSettings(Configuration);
            await Clients.Caller.dialog(new Dialog(AlertType.Info) { Header = "Reload configuration", Question = "Reload successful", Answer1 = "Ok" });
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Elevated, AuthenticationType = AuthenticationType.Token)]
        public async Task CleanUpFiles(string token, bool delete = false)
        {
            List<string> files = new List<string>();
            if (Directory.Exists(General.FilePath))
            {
                files = Directory.GetFiles(General.FilePath).ToList();
            }

            if (Directory.Exists(General.TemporaryFilePath))
            {
                files.AddRange(Directory.GetFiles(General.TemporaryFilePath));
            }

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
                        {
                            if (delete)
                            {
                                File.Delete(file);
                            }
                            else
                            {
                                text += "\n" + $"{file} - {new FileInfo(file).Name}";
                            }
                        }
                    }
                    if (delete)
                    {
                        await Clients.Caller.dialog(new Dialog(AlertType.Info) { Header = "Clean up", Question = $"Removed {result?.Count()} files", Answer1 = "Ok" });
                    }
                    else
                    {
                        await Clients.Caller.dialog(new Dialog(AlertType.Info) { Header = "Clean up", Question = text, Answer1 = "Ok" });
                    }
                }
            }
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task ChangeDownload(string token, int fileId, string name)
        {
            if (name == null || name.Length <= 2)
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Danger) { Header = "Error", Question = "Filename has to be at least 3 characters long", Answer1 = "Ok" });
                return;
            }
            var dbUser = _postgres.Users.Include(x => x.RememberTokens).FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == token));
            var result = _postgres.Users.Include(x => x.Files).FirstOrDefault(x => x.ID == dbUser.ID)?.Files.FirstOrDefault(x => x.ID == fileId);
            result.Name = name;
            await _postgres.SaveChangesAsync();
            if (result.DbFileFolderId > 0)
            {
                await GetFolderFiles(token, result.DbFileFolderId);
            }
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task GetDownloads(string token)
        {
            var dbUser = _postgres.Users.Include(x => x.RememberTokens).FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == token));
            var userFiles = _postgres.Files?.Where(x => x.DbUserID == dbUser.ID).ToList();
            var result = userFiles.Select(x => new FileDto(x)).OrderBy(x => x.Name).ToList();
            await Clients.Caller.getDownloads(result);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task GetFolders(string token, int folderId = 1)
        {
            var dbUser = _postgres.Users.Include(x => x.RememberTokens).FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == token));
            var shareFolders = _postgres.FolderShare?.Where(x => x.DbUserID == dbUser.ID);
            var folder = _postgres.Folders?.Where(x => x.DbUserID == null || x.DbUserID == dbUser.ID || shareFolders.FirstOrDefault(y => y.DbFolderID == x.Id) != null || shareFolders.FirstOrDefault(y => y.DbFolderID == x.ParentId) != null).OrderBy(x => x.Name).ToList();
            var resultFolder = folder.FirstOrDefault(x => x.Id == folderId);
            if (resultFolder != null)
            {
                var folderResult = new FolderDto(resultFolder);
                await Clients.Group(token).getFolders(folderResult);
            }
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task AddFolder(string token, int folderId)
        {
            var dbUser = _postgres.Users.Include(x => x.RememberTokens).FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == token));
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
            await GetFolders(token, folderId);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task DeleteFolder(string token, int folderId)
        {
            var dbUser = _postgres.Users.Include(x => x.RememberTokens).FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == token));
            var folder = _postgres.Folders?.Include(x => x.Files).FirstOrDefault(x => x.Id == folderId && (x.DbUserID == dbUser.ID || dbUser.userprivileges > UserPrivileges.Administrator));
            if (folder != null)
            {
                if (folder.Files?.Count > 0 || _postgres.Folders?.Where(x => x.ParentId == folderId).Count() > 0)
                {
                    await Clients.Caller.dialog(new Dialog(AlertType.Warning) { Header = "Folder deletion", Question = "Prevented folder deletion because it still contains files", Answer1 = "Ok" });
                    return;
                }
                _postgres.Folders.Remove(folder);
                await _postgres.SaveChangesAsync();
                await GetFolders(token, (int)(folder.ParentId.HasValue ? folder.ParentId : 1));
            }
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task DownloadFile(string token, string url, string fileName, ConversionPreset preset = ConversionPreset.SuperFast)
        {
            var dbUser = await _postgres.Users.Include(x => x.RememberTokens).FirstOrDefaultAsync(x => x.RememberTokens.Any(y => y.Token == token));
            if (dbUser.userprivileges >= UserPrivileges.Administrator)
            {
                _manager.AddDownload(new(dbUser.ID, fileName, token, url, preset));
            }
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async void DownloadYtVideo(string token, string url, string quality = "1080", bool audioOnly = false)
        {
            var dbUser = await _postgres.Users.Include(x => x.RememberTokens).FirstOrDefaultAsync(x => x.RememberTokens.Any(y => y.Token == token));
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
                    {
                        break;
                    }

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

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task GetFileInfo(string token, int id)
        {
            var dbUser = await _postgres.Users.Include(x => x.RememberTokens).FirstOrDefaultAsync(x => x.RememberTokens.Any(y => y.Token == token));
            var file = _postgres.Files.ToList().FirstOrDefault(x => x.ID == id);
            if (file == null)
            {
                await _manager.SendDefaultDialog(dbUser.ID.ToString(), "File not found in database", AlertType.Warning);
                return;
            }

            var path = $"{(file.Temporary ? General.TemporaryFilePath : General.FilePath)}/{file.FileKey}{file.FileEnding}";
            if (!File.Exists(path))
            {
                await _manager.SendDefaultDialog(dbUser.ID.ToString(), $"File {path} not found", AlertType.Warning);
                return;
            }

            var mediaInfo = await Xabe.FFmpeg.FFmpeg.GetMediaInfo(path);
            if (mediaInfo == null)
            {
                await _manager.SendDefaultDialog(dbUser.ID.ToString(), "Media info was null", AlertType.Warning);
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

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task ShareFolder(string token, int folderId, int userId)
        {
            var dbUser = await _postgres.Users.Include(x => x.RememberTokens).FirstOrDefaultAsync(x => x.RememberTokens.Any(y => y.Token == token));
            if (dbUser.ID == folderId)
            {
                await _manager.SendDefaultDialog(dbUser.ID.ToString(), "Can't share folders with yourself", AlertType.Warning);
                return;
            }

            var shareUser = _postgres.Users?.FirstOrDefault(x => x.ID == userId);
            if (shareUser == null)
            {
                await _manager.SendDefaultDialog(dbUser.ID.ToString(), "Share user does not exist", AlertType.Warning);
                return;
            }

            var shareFolder = _postgres.Folders?.FirstOrDefault(x => x.Id == folderId);
            if (shareFolder == null)
            {
                await _manager.SendDefaultDialog(dbUser.ID.ToString(), "Share folder does not exist", AlertType.Warning);
                return;
            }

            if (shareFolder.DbUserID != dbUser.ID)
            {
                await _manager.SendDefaultDialog(dbUser.ID.ToString(), $"Can't share folder {shareFolder.Name} because you don't have ownership", AlertType.Warning);
                return;
            }

            var oldFolderShare = _postgres.FolderShare?.FirstOrDefault(x => x.DbFolderID == shareFolder.Id && x.DbUserID == shareUser.ID);
            if (oldFolderShare != null)
            {
                await _manager.SendDefaultDialog(dbUser.ID.ToString(), $"Not sharing {shareFolder.Name} with {shareUser.username} anymore!", AlertType.Info);
                _postgres.FolderShare?.Remove(oldFolderShare);
                await _postgres.SaveChangesAsync();
                return;
            }
            var newShare = new DbFolderUserShare(shareUser.ID, shareFolder.Id);
            _postgres.FolderShare?.Add(newShare);
            await _postgres.SaveChangesAsync();
            await _manager.SendDefaultDialog(dbUser.ID.ToString(), $"Now sharing {shareFolder.Name} with {shareUser.username}!", AlertType.Success);
            await GetFolders(shareUser.RememberTokens.OrderByDescending(x => x.Created).FirstOrDefault()?.Token, shareFolder.Id);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task GetFolderFiles(string token, int folderId)
        {
            var dbUser = await _postgres.Users.Include(x => x.RememberTokens).FirstOrDefaultAsync(x => x.RememberTokens.Any(y => y.Token == token));
            var dbFolder = _postgres.Folders?.Where(x => x.Id == folderId).FirstOrDefault();
            if (dbFolder == null)
            {
                await _manager.SendDefaultDialog(dbUser.ID.ToString(), $"Could not find folder", AlertType.Warning);
                return;
            }
            var shareFolders = _postgres.FolderShare?.Where(x => x.DbUserID == dbUser.ID);
            var files = _postgres.Files?.Where(x => x.DbFileFolderId == folderId && (x.DbUserID == dbUser.ID || shareFolders.FirstOrDefault(y => y.DbFolderID == folderId) != null || shareFolders.FirstOrDefault(y => y.DbFolderID == dbFolder.ParentId) != null || dbFolder.DbUserID == dbUser.ID));
            if (files != null)
            {
                await Clients.Caller.getFolderFiles(files.Select(x => new FileDto(x)).ToList());
            }
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task ChangeFolderName(string token, int folderId, string folderName)
        {
            var folder = _postgres.Folders?.FirstOrDefault(x => x.Id == folderId);
            if (folder != null)
            {
                folder.Name = folderName;
                await _postgres.SaveChangesAsync();
                await GetFolders(token, (int)folder.ParentId);
            }
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task ChangeFolder(string token, int fileId, int folderId)
        {
            var file = _postgres.Files?.FirstOrDefault(x => x.ID == fileId);
            if (file != null && file.DbFileFolderId != folderId)
            {
                file.DbFileFolderId = folderId;
                await _postgres.SaveChangesAsync();
            }
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task CancelConversion(string token, string downloadId)
        {
            var dbUser = await _postgres.Users.Include(x => x.RememberTokens).FirstOrDefaultAsync(x => x.RememberTokens.Any(y => y.Token == token));
            await _manager.CancelM3U8Conversion(dbUser.ID, downloadId);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task RemoveFile(string token, int id)
        {
            var dbUser = await _postgres.Users.Include(x => x.RememberTokens).FirstOrDefaultAsync(x => x.RememberTokens.Any(y => y.Token == token));
            var file = _postgres.Files.ToList().FirstOrDefault(x => x.ID == id);
            if (file != null && (file.DbUserID == dbUser.ID || dbUser.userprivileges >= UserPrivileges.Elevated))
            {
                var path = $"{(file.Temporary ? General.TemporaryFilePath : General.FilePath)}/{file.FileKey}{file.FileEnding}";
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                _postgres.Files.Remove(file);
                await _postgres.SaveChangesAsync();
                await Clients.Caller.downloadRemoved(id.ToString());
                if (file.DbFileFolderId > 0)
                {
                    await GetFolderFiles(token, file.DbFileFolderId);
                }
            }
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task MakeFilePermanent(string token, int id)
        {
            var dbUser = await _postgres.Users.Include(x => x.RememberTokens).FirstOrDefaultAsync(x => x.RememberTokens.Any(y => y.Token == token));
            var file = _postgres.Files.ToList().FirstOrDefault(x => x.ID == id);
            if (file != null && file.Temporary && (file.DbUserID == dbUser.ID || dbUser.userprivileges >= UserPrivileges.Elevated))
            {
                file.Temporary = false;
                file.DateToBeDeleted = null;
                await _postgres.SaveChangesAsync();
                await GetFolderFiles(token, file.DbFileFolderId);
            }
        }
    }
}
