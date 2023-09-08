using iText.Layout.Splitting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScreenIT.Helper;
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace SyncStreamAPI.ServerData
{
    public class MainManager
    {
        public static BlockingCollection<Room> Rooms { get; set; } = new BlockingCollection<Room>();
        public static GeneralManager GeneralManager { get; set; }
        public static RoomManager RoomManager { get; set; }
        public static GeneralManager GetGeneralManager() => GeneralManager;
        public static RoomManager GetRoomManager() => RoomManager;
        public static Room GetRoom(string UniqueId) => RoomManager.GetRoom(UniqueId);
        public static BlockingCollection<Room> GetRooms() => RoomManager.GetRooms();
        public BlockingCollection<LiveUser> LiveUsers { get; set; } = new BlockingCollection<LiveUser>();
        public Dictionary<WebClient, DownloadClientValue> userWebDownloads { get; set; } = new Dictionary<WebClient, DownloadClientValue>();
        public Dictionary<CancellationTokenSource, BlockingCollection<DownloadClientValue>> userYtPlaylistDownload { get; set; } = new Dictionary<CancellationTokenSource, BlockingCollection<DownloadClientValue>>();
        public BlockingCollection<DownloadClientValue> userDownloads { get; set; } = new BlockingCollection<DownloadClientValue>();
        public BlockingCollection<DownloadClientValue> userYtDownloads { get; set; } = new BlockingCollection<DownloadClientValue>();
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
        public void YtPlaylistDownload(List<DownloadClientValue> vids)
        {
            var tokens = vids.Select(x => x.CancellationToken.Token).ToArray();
            var linkedTokens = CancellationTokenSource.CreateLinkedTokenSource(tokens);
            var blockingCollection = new BlockingCollection<DownloadClientValue>();
            vids.ForEach(v => blockingCollection.Add(v));
            userYtPlaylistDownload.Add(linkedTokens, blockingCollection);
            foreach (var vid in vids)
            {
                _ = YtDownload(vid, linkedTokens);
            }
        }

        [ErrorHandling]
        public async Task YtDownload(DownloadClientValue downloadClient, CancellationTokenSource tokenSource = null)
        {
            userYtDownloads.Add(downloadClient);
            if (userYtDownloads.Count > General.MaxParallelYtDownloads)
            {
                return;
            }
            await RunYtDownload(downloadClient);
            if (downloadClient.CancellationToken.IsCancellationRequested && userYtPlaylistDownload.ContainsKey(tokenSource))
            {
                var downloads = userYtPlaylistDownload[tokenSource];
                userYtDownloads.Where(x => downloads.FirstOrDefault(y => y.UniqueId == x.UniqueId) != null).ToList().ForEach(x => x.CancellationToken.Cancel());
            }
            using (var scope = ServiceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                await _hub.Clients.Group(downloadClient.UserId.ToString()).downloadFinished(downloadClient.UniqueId);
            }
            userYtDownloads.TryTake(out downloadClient);
            await StartNextYtDownload();
        }

        private async Task RunYtDownload(DownloadClientValue downloadClient)
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var userId = downloadClient.UserId.ToString();
                var dbUser = _postgres.Users
                    .Include(x => x.RememberTokens)
                    .FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == downloadClient.Token) == true);
                var fileExtension = downloadClient.AudioOnly ? ".mp3" : ".mp4";
                var dbFile = new DbFile(downloadClient.FileName, fileExtension, dbUser);
                var filePath = dbFile.GetPath();
                var ytdl = General.GetYoutubeDL();
                ytdl.OutputFolder = General.FilePath;
                ytdl.RestrictFilenames = true;
                ytdl.OutputFileTemplate = $"{dbFile.FileKey}{fileExtension}";
                Timer _progressTimer = null;
                var progress = new Progress<DownloadProgress>(p =>
                {
                    _progressTimer ??= new Timer(async _ =>
                    {
                        if (downloadClient.CancellationToken.IsCancellationRequested)
                            return;
                        await YtDLPHelper.UpdateDownloadProgress(downloadClient, _hub, p);
                        _progressTimer?.Dispose();
                        _progressTimer = null;
                    }, null, TimeSpan.FromSeconds(1), TimeSpan.Zero);
                });
                downloadClient.Stopwatch = Stopwatch.StartNew();
                RunResult<string> runResult = null;
                await _hub.Clients.Group(userId).downloadListen(downloadClient.UniqueId);
                try
                {
                    runResult = await YtDLPHelper.DownloadMedia(ytdl, downloadClient, downloadClient.AudioOnly, progress);
                    if (runResult != null && runResult?.Success == true)
                    {
                        dbUser.Files.Add(dbFile);
                        await _postgres.SaveChangesAsync();
                        Console.WriteLine($"User {downloadClient.UserId} saved {downloadClient.FileName} to DB");
                    }
                    else
                    {
                        Console.WriteLine($"Error downloading {downloadClient.Url}: {runResult?.ErrorOutput.FirstOrDefault()}");
                        FileCheck.CheckOverrideFile(dbFile.GetPath());
                    }
                }
                catch (TaskCanceledException ex)
                {
                    Console.WriteLine("Download cancelled by user");
                    FileCheck.CheckOverrideFile(dbFile.GetPath());
                }
            }
        }

        public async void AddDownload(DownloadClientValue downloadClient)
        {
            using var scope = ServiceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            using var browser = scope.ServiceProvider.GetRequiredService<BrowserAutomation>();
            if (downloadClient.Url.Contains("m3u8"))
            {
                _ = YtDownload(downloadClient);
                return;
            }

            var webClient = new WebClient();
            webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
            webClient.DownloadDataCompleted += WebClient_DownloadDataCompleted;
            webClient.Headers.Set("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:106.0) Gecko/20100101 Firefox/106.0");
            userWebDownloads.Add(webClient, downloadClient);
            try
            {
                await using var stream = webClient.OpenRead(downloadClient.Url);
                var totalDownload = Convert.ToInt64(webClient.ResponseHeaders["Content-Length"]);
                var mb = (totalDownload / 1024d / 1024d);
                if (totalDownload <= 0)
                {
                    if (browser == null)
                    {
                        await SendDefaultDialog(downloadClient.UserId.ToString(),
                            $"Not allowed to download anything above 500mb file was {mb}mb", AlertType.Danger);
                        return;
                    }

                    var response = await browser.GetM3U8FromUrl(downloadClient.Url);
                    if (response != null)
                    {
                        await hub.Clients.Group(downloadClient.UserId.ToString())
                            .browserResults(response.OutputUrls);
                    }

                    return;
                }

                webClient.DownloadDataAsync(new Uri(downloadClient.Url));
            }
            catch (Exception ex)
            {
                userWebDownloads.Remove(webClient);
                webClient.Dispose();
                await hub.Clients.Group(downloadClient.UserId.ToString()).dialog(new Dialog(AlertType.Danger)
                    { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                Console.WriteLine(ex.ToString());
                return;
            }

            return;
        }

        public async Task RunM3U8Conversion(DownloadClientValue downloadClient)
        {
            await RunYtDownload(downloadClient);
            return;
        }

        private async void SendStatusToM3U8Clients()
        {
            using var scope = ServiceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            var waitingClients = userDownloads.Where(x => !x.Running).ToList();
            for (int i = 0; i < waitingClients.Count; i++)
            {
                if (!waitingClients[i].CancellationToken.IsCancellationRequested)
                {
                    var clientResult = new DownloadInfo(i > 0 ? $"{i} download{((i) > 1 ? "s" : "")} infront of you" : $"You're up next, please wait...", waitingClients[i].FileName, waitingClients[i].UniqueId);
                    await hub.Clients.Group(waitingClients[i].UserId.ToString()).downloadProgress(clientResult);
                }
            }
        }

        public Task CancelM3U8Conversion(int userId, string downloadId)
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var downloadClient = userDownloads.FirstOrDefault(x => x.UniqueId == downloadId);
                if (downloadClient != null && downloadClient.UserId == userId && !downloadClient.CancellationToken.IsCancellationRequested)
                {
                    downloadClient.StopKeepAlive();
                    downloadClient.CancellationToken.Cancel();
                }
                var downloadYtClient = userYtDownloads.FirstOrDefault(x => x.UniqueId == downloadId);
                if (downloadYtClient == null || downloadYtClient.UserId != userId ||
                    downloadYtClient.CancellationToken.IsCancellationRequested) return Task.CompletedTask;
                downloadYtClient.StopKeepAlive();
                downloadYtClient.CancellationToken.Cancel();
            }

            return Task.CompletedTask;
        }

        public async Task StartNextDownload()
        {
            using var scope = ServiceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            if (userDownloads.Count > 0)
            {
                SendStatusToM3U8Clients();
                var runningDls = userDownloads.Where(x => x.Running).Count();
                while (runningDls > 0 && runningDls < General.MaxParallelYtDownloads && !userDownloads.All(x => x.Running))
                {
                    var nextDownload = userDownloads.FirstOrDefault(x => !x.Running);
                    if (nextDownload == null) continue;
                    var clientResult = new DownloadInfo("Your download is starting, please wait...", nextDownload.FileName, nextDownload.UniqueId);
                    await hub.Clients.Group(nextDownload.UserId.ToString()).downloadProgress(clientResult);
                    _ = RunM3U8Conversion(nextDownload);
                }
            }
        }

        private async Task StartNextYtDownload()
        {
            using var scope = ServiceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            if (userYtDownloads.Count > 0)
            {
                var cancelledDownloads = userYtDownloads.Where(x => x.CancellationToken.IsCancellationRequested).ToList();
                cancelledDownloads.ForEach(x => userYtDownloads.TryTake(out x));
                while (userYtDownloads.Count(x => x.Running) < General.MaxParallelYtDownloads && !userYtDownloads.All(x => x.Running))
                {
                    var nextDownload = userYtDownloads.FirstOrDefault(x => !x.Running && !x.CancellationToken.IsCancellationRequested);
                    if (nextDownload == null) continue;
                    nextDownload.Running = true;
                    var clientResult = new DownloadInfo("Your download is starting, please wait...", nextDownload.FileName, nextDownload.UniqueId);
                    await hub.Clients.Group(nextDownload.UserId.ToString()).downloadProgress(clientResult);
                    _ = RunYtDownload(nextDownload);
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

        private async void SaveFileToFilesystem(DownloadClientValue client, byte[] file)
        {
            using var scope = ServiceProvider.CreateScope();
            var postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            try
            {

                var dbUser = postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == client.Token)).FirstOrDefault();
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
                    await postgres.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                await hub.Clients.Group(client.UserId.ToString()).dialog(new Dialog(AlertType.Danger) { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                Console.WriteLine(ex.ToString());
            }
            await hub.Clients.Group(client.UserId.ToString()).downloadFinished(client.UniqueId);
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
