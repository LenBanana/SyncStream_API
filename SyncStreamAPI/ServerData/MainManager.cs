using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using YoutubeDLSharp;

namespace SyncStreamAPI.ServerData
{
    public class MainManager
    {
        public static List<Room> Rooms { get; set; } = new List<Room>();
        public static GeneralManager GeneralManager { get; set; }
        public static RoomManager RoomManager { get; set; }
        public static GeneralManager GetGeneralManager() => GeneralManager;
        public static RoomManager GetRoomManager() => RoomManager;
        public static Room GetRoom(string UniqueId) => RoomManager.GetRoom(UniqueId);
        public static List<Room> GetRooms() => RoomManager.GetRooms();
        public List<LiveUser> LiveUsers { get; set; } = new List<LiveUser>();
        public static bool checking { get; set; } = false;
        public Dictionary<WebClient, DownloadClientValue> userWebDownloads { get; set; } = new Dictionary<WebClient, DownloadClientValue>();
        public List<DownloadClientValue> userM3U8Conversions { get; set; } = new List<DownloadClientValue>();
        public static IServiceProvider ServiceProvider { get; set; }
        IConfiguration Configuration { get; }
        public MainManager(IServiceProvider provider)
        {
            FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official).Wait();
            var path = FFmpeg.ExecutablesPath;
            if (path == null)
            {
                FFmpeg.SetExecutablesPath(Directory.GetCurrentDirectory());
            }

            LinuxBash.DownloadYtDlp().Wait();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                LinuxBash.Bash($"chmod +x /app/ffmpeg && chmod +x /app/ffprobe");
                LinuxBash.Bash($"chmod +x /app/yt-dlp");
                LinuxBash.Bash($"alias yt-dlp='python3 /app/yt-dlp'");
            }
            ServiceProvider = provider;
            using (var scope = ServiceProvider.CreateScope())
            {
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                Configuration = config;
            }
            GeneralManager = new GeneralManager(ServiceProvider, Rooms);
            RoomManager = new RoomManager(ServiceProvider, Rooms);
            GeneralManager.ReadSettings(Configuration);
            GeneralManager.AddDefaultRooms();
        }

        public async Task SendDefaultDialog(string group, string message, Enums.AlertType alertType, string header = "")
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                await _hub.Clients.Group(group).dialog(new Dialog(alertType) { Header = header.Length > 0 ? header : "Servermessage", Question = message, Answer1 = "Ok" });
            }
        }

        [ErrorHandling]
        public async Task YtDownload(DownloadClientValue downloadClient, bool audioOnly = false)
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var userId = downloadClient.UserId.ToString();
                await _hub.Clients.Group(userId).downloadListen(downloadClient.UniqueId);

                var dbUser = _postgres.Users
                    .Include(x => x.RememberTokens)
                    .FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == downloadClient.Token) == true);

                var fileExtension = audioOnly ? ".mp3" : ".mp4";
                var dbFile = new DbFile(downloadClient.FileName, fileExtension, dbUser);
                var filePath = dbFile.GetPath();

                var ytdl = General.GetYoutubeDL();
                ytdl.OutputFolder = General.FilePath;
                ytdl.RestrictFilenames = true;
                ytdl.OutputFileTemplate = $"{dbFile.FileKey}{fileExtension}";

                var progress = new Progress<DownloadProgress>(async p => await YtDLPHelper.UpdateDownloadProgress(downloadClient, _hub, p));

                downloadClient.Stopwatch = Stopwatch.StartNew();
                RunResult<string> runResult = await YtDLPHelper.DownloadMedia(ytdl, downloadClient, audioOnly, progress);

                await _hub.Clients.Group(userId).downloadFinished(downloadClient.UniqueId);

                if (runResult?.Success == true)
                {
                    dbUser.Files.Add(dbFile);
                    await _postgres.SaveChangesAsync();
                    Console.WriteLine($"User {downloadClient.UserId} saved {downloadClient.FileName} to DB");
                }
                else
                {
                    Console.WriteLine($"Error downloading {downloadClient.Url}: {runResult?.ErrorOutput.FirstOrDefault()}");
                }
            }
        }

        public async void AddDownload(DownloadClientValue downloadClient)
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var _browser = scope.ServiceProvider.GetRequiredService<BrowserAutomation>();
                if (downloadClient.Url.Contains("m3u8"))
                {
                    userM3U8Conversions.Add(downloadClient);
                    SendStatusToM3U8Clients();
                    if (userM3U8Conversions.Count > General.MaxParallelConversions)
                    {
                        return;
                    }

                    RunM3U8Conversion(downloadClient);
                    return;
                }
                var webClient = new WebClient();
                webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
                webClient.DownloadDataCompleted += WebClient_DownloadDataCompleted;
                webClient.Headers.Set("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:106.0) Gecko/20100101 Firefox/106.0");
                userWebDownloads.Add(webClient, downloadClient);
                try
                {
                    using (var stream = webClient.OpenRead(downloadClient.Url))
                    {
                        var totalDownload = Convert.ToInt64(webClient.ResponseHeaders["Content-Length"]);
                        var mb = (totalDownload / 1024d / 1024d);
                        if (totalDownload <= 0)
                        {
                            if (_browser == null)
                            {
                                await SendDefaultDialog(downloadClient.UserId.ToString(), $"Not allowed to download anything above 500mb file was {mb}mb", AlertType.Danger);
                                return;
                            }
                            var response = await _browser.GetM3U8FromUrl(downloadClient.Url);
                            if (response != null)
                            {
                                await _hub.Clients.Group(downloadClient.UserId.ToString()).browserResults(response.OutputUrls);
                            }

                            return;
                        }
                        webClient.DownloadDataAsync(new Uri(downloadClient.Url));
                    }
                }
                catch (Exception ex)
                {
                    userWebDownloads.Remove(webClient);
                    webClient.Dispose();
                    await _hub.Clients.Group(downloadClient.UserId.ToString()).dialog(new Dialog(AlertType.Danger) { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                    Console.WriteLine(ex.ToString());
                    return;
                }
            }
            return;
        }

        public async void RunM3U8Conversion(DownloadClientValue downloadClient)
        {
            downloadClient.Stopwatch = Stopwatch.StartNew();
            downloadClient.Running = true;
            using (var scope = ServiceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                await _hub.Clients.Group(downloadClient.UserId.ToString()).downloadListen(downloadClient.UniqueId);
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == downloadClient.Token)).FirstOrDefault();
                var dbFile = new DbFile(downloadClient.FileName, ".mp4", dbUser);
                var filePath = dbFile.GetPath();
                try
                {
                    if (dbUser == null)
                    {
                        throw new Exception($"Unable to find user");
                    }

                    var conversion = (await FFmpeg.Conversions.FromSnippet.SaveM3U8Stream(new Uri(downloadClient.Url), filePath)).UseMultiThread(true).SetOverwriteOutput(true);
                    conversion.OnProgress += async (sender, args) =>
                    {
                        try
                        {
                            var text = $"{args.Duration}/{args.TotalLength} - {StopwatchCalc.CalculateRemainingTime(downloadClient.Stopwatch, args.Percent)} remaining";
                            var result = new DownloadInfo(text, downloadClient.FileName, downloadClient.UniqueId) { Progress = args.Percent };
                            await _hub.Clients.Group(downloadClient.UserId.ToString()).downloadProgress(result);
                        }
                        catch (Exception ex) { Console.WriteLine(ex.ToString()); }
                    };
                    if (downloadClient?.CancellationToken?.Token != null)
                    {
                        await conversion.UseMultiThread(true).SetPreset(downloadClient.Preset).Start(downloadClient.CancellationToken.Token);
                    }
                    else
                    {
                        throw new OperationCanceledException();
                    }

                    if (!File.Exists(filePath))
                    {
                        await _hub.Clients.Group(downloadClient.UserId.ToString()).dialog(new Dialog(AlertType.Danger) { Header = "Error", Question = "There has been an error trying to save the file", Answer1 = "Ok" });
                        return;
                    }
                    if (!downloadClient.CancellationToken.IsCancellationRequested)
                    {
                        dbUser.Files.Add(dbFile);
                        await _postgres.SaveChangesAsync();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    var header = ex?.InnerException?.GetType()?.Name;
                    var msg = ex?.Message;
                    await _hub.Clients.Group(downloadClient.UserId.ToString()).dialog(new Dialog(AlertType.Danger) { Header = header, Question = $"{header} \n{msg}", Answer1 = "Ok" });
                    Console.WriteLine(ex.ToString());
                }
                try
                {
                    if (File.Exists(filePath) && dbUser.Files.FirstOrDefault(x => x.FileKey == dbFile.FileKey) == null)
                    {
                        File.Delete(filePath);
                    }
                }
                catch { }
                await _hub.Clients.Group(downloadClient.UserId.ToString()).downloadFinished(downloadClient.UniqueId);
                if (userM3U8Conversions.Contains(downloadClient))
                {
                    userM3U8Conversions.Remove(downloadClient);
                }

                StartNextDownload();
            }
        }

        public async void SendStatusToM3U8Clients()
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var waitingClients = userM3U8Conversions.Where(x => !x.Running).ToList();
                for (int i = 0; i < waitingClients.Count; i++)
                {
                    if (!waitingClients[i].CancellationToken.IsCancellationRequested)
                    {
                        var clientResult = new DownloadInfo(i > 0 ? $"{i} download{((i) > 1 ? "s" : "")} infront of you" : $"You're up next, please wait...", waitingClients[i].FileName, waitingClients[i].UniqueId);
                        await _hub.Clients.Group(waitingClients[i].UserId.ToString()).downloadProgress(clientResult);
                    }
                }
            }
        }

        public async Task CancelM3U8Conversion(int userId, string downloadId)
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var idx = userM3U8Conversions.FindIndex(x => x.UniqueId == downloadId);
                if (idx >= 0 && userM3U8Conversions[idx].UserId == userId)
                {
                    userM3U8Conversions[idx].CancellationToken.Cancel();
                    userM3U8Conversions[idx].StopKeepAlive();
                    userM3U8Conversions.RemoveAt(idx);
                    await _hub.Clients.Group(userId.ToString()).downloadFinished(downloadId);
                }
            }
        }

        public async void StartNextDownload()
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                if (userM3U8Conversions.Count > 0)
                {
                    SendStatusToM3U8Clients();
                    if (userM3U8Conversions.Where(x => x.Running).Count() < General.MaxParallelConversions)
                    {
                        var nextDownload = userM3U8Conversions.FirstOrDefault(x => !x.Running);
                        if (nextDownload != null)
                        {
                            var clientResult = new DownloadInfo("Your download is starting, please wait...", nextDownload.FileName, nextDownload.UniqueId);
                            await _hub.Clients.Group(nextDownload.UserId.ToString()).downloadProgress(clientResult);
                            RunM3U8Conversion(nextDownload);
                        }
                    }
                }
            }
        }

        private void WebClient_DownloadDataCompleted(object sender, DownloadDataCompletedEventArgs e)
        {
            var client = sender as WebClient;
            var id = userWebDownloads[client];
            userWebDownloads.Remove(client);
            client.Dispose();
            SaveFileToFilesystem(id, e.Result);
        }

        public async void SaveFileToFilesystem(DownloadClientValue client, byte[] file)
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                try
                {

                    var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == client.Token)).FirstOrDefault();
                    if (dbUser == null)
                    {
                        return;
                    }

                    DbRememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == client.Token);
                    if (Token == null)
                    {
                        return;
                    }

                    if (dbUser.userprivileges >= UserPrivileges.Administrator)
                    {
                        if (!Directory.Exists(General.FilePath))
                        {
                            Directory.CreateDirectory(General.FilePath);
                        }

                        var dbFile = new DbFile(client.FileName, $".{client.Url.Split('.').Last()}", dbUser);
                        var filePath = dbFile.GetPath();
                        File.WriteAllBytes(filePath, file);
                        dbUser.Files.Add(dbFile);
                        await _postgres.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    await _hub.Clients.Group(client.UserId.ToString()).dialog(new Dialog(AlertType.Danger) { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                    Console.WriteLine(ex.ToString());
                }
                await _hub.Clients.Group(client.UserId.ToString()).downloadFinished(client.UniqueId);
            }
        }

        private async void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            try
            {
                using var scope = ServiceProvider.CreateScope();
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var id = userWebDownloads[sender as WebClient];
                var perc = Math.Max(0, e.BytesReceived / (double)e.TotalBytesToReceive * 100);
                var timeLeft = TimeSpan.FromMilliseconds(id.Stopwatch.ElapsedMilliseconds / perc * (100 - perc)).ToString(@"HH\:mm\:ss");
                var result = new DownloadInfo($"{Math.Round(e.BytesReceived / 1048576d, 2)}MB of {Math.Round(e.TotalBytesToReceive / 1048576d, 2)}MB - {timeLeft} remaining", id.FileName, id.UniqueId) { Id = id.UniqueId, Progress = perc };
                await _hub.Clients.Group(id.UserId.ToString()).downloadProgress(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
