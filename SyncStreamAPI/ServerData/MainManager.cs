#nullable enable
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
using SyncStreamAPI.Helper.StreamingSites;
using SyncStreamAPI.Models.Interfaces;
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
        public static Room? GetRoom(string UniqueId) => RoomManager.GetRoom(UniqueId);
        public static BlockingCollection<Room> GetRooms() => RoomManager.GetRooms();
        public BlockingCollection<LiveUser> LiveUsers { get; set; } = new BlockingCollection<LiveUser>();

        public Dictionary<WebClient, DownloadClientValue> userWebDownloads { get; set; } =
            new Dictionary<WebClient, DownloadClientValue>();

        public Dictionary<CancellationTokenSource, BlockingCollection<DownloadClientValue>>
            userYtPlaylistDownload { get; set; } =
            new Dictionary<CancellationTokenSource, BlockingCollection<DownloadClientValue>>();

        public BlockingCollection<DownloadClientValue> userDownloads { get; set; } =
            new BlockingCollection<DownloadClientValue>();

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
        
        public static async Task<DbUser?> GetUser(string token)
        {
            using var scope = ServiceProvider.CreateScope();
            var postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
            var dbUser = await postgres.Users
                .Include(x => x.RememberTokens)
                .FirstOrDefaultAsync(x => x.RememberTokens.Any(y => y.Token == token));
            return dbUser;
        }

        public async Task SendDefaultDialog(string group, string message, Enums.AlertType alertType, string header = "")
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                await _hub.Clients.Group(group).dialog(new Dialog(alertType)
                    { Header = header.Length > 0 ? header : "Servermessage", Question = message, Answer1 = "Ok" });
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
                _ = YtDlpDownload(vid, linkedTokens);
            }
        }

        [ErrorHandling]
        public async Task YtDlpDownload(DownloadClientValue downloadClient, CancellationTokenSource tokenSource = null)
        {
            userDownloads.Add(downloadClient);
            if (userDownloads.Count > General.MaxParallelYtDownloads)
            {
                return;
            }

            if (downloadClient.CancellationToken.IsCancellationRequested &&
                userYtPlaylistDownload.ContainsKey(tokenSource))
            {
                var downloads = userYtPlaylistDownload[tokenSource];
                userDownloads.Where(x => downloads.FirstOrDefault(y => y.UniqueId == x.UniqueId) != null).ToList()
                    .ForEach(x => x.CancellationToken.Cancel());
                using var scope = ServiceProvider.CreateScope();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                await hub.Clients.Group(downloadClient.UserId.ToString()).downloadFinished(downloadClient.UniqueId);
            }
            await StartNextYtDownload();
        }

        private async Task RunYtDlpDownload(DownloadClientValue downloadClient)
        {
            using var scope = ServiceProvider.CreateScope();
            var postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            var userId = downloadClient.UserId.ToString();
            var dbUser = postgres.Users
                .Include(x => x.RememberTokens)
                .FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == downloadClient.Token) == true);
            var fileExtension = downloadClient.AudioOnly ? ".mp3" : ".mp4";
            var dbFile = new DbFile(downloadClient.FileName, fileExtension, dbUser);
            var filePath = dbFile.GetPath();
            var ytdl = General.GetYoutubeDL();
            ytdl.OutputFolder = General.FilePath;
            ytdl.RestrictFilenames = true;
            ytdl.OutputFileTemplate = $"{dbFile.FileKey}{fileExtension}";
            Timer progressTimer = null;
            var progress = new Progress<DownloadProgress>(p =>
            {
                progressTimer ??= new Timer(async _ =>
                {
                    if (downloadClient.CancellationToken.IsCancellationRequested)
                        return;
                    await YtDLPHelper.UpdateDownloadProgress(downloadClient, hub, p);
                    progressTimer?.Dispose();
                    progressTimer = null;
                }, null, TimeSpan.FromSeconds(1), TimeSpan.Zero);
            });
            downloadClient.Stopwatch = Stopwatch.StartNew();
            RunResult<string> runResult = null;
            await hub.Clients.Group(userId).downloadListen(downloadClient.UniqueId);
            try
            {
                runResult = await YtDLPHelper.DownloadMedia(ytdl, downloadClient, downloadClient.AudioOnly, progress);
                if (runResult != null && runResult?.Success == true)
                {
                    dbUser.Files.Add(dbFile);
                    await postgres.SaveChangesAsync();
                    Console.WriteLine($"User {downloadClient.UserId} saved {downloadClient.FileName} to DB");
                }
                else
                {
                    Console.WriteLine(
                        $"Error downloading {downloadClient.Url}: {runResult?.ErrorOutput.FirstOrDefault()}");
                    FileCheck.CheckOverrideFile(dbFile.GetPath());
                }
            }
            catch (TaskCanceledException ex)
            {
                Console.WriteLine("Download cancelled by user");
                FileCheck.CheckOverrideFile(dbFile.GetPath());
            }
            await hub.Clients.Group(downloadClient.UserId.ToString()).downloadFinished(downloadClient.UniqueId);
            userDownloads.TryTake(out downloadClient);
            await StartNextYtDownload();
        }

        public async void AddDownload(DownloadClientValue downloadClient)
        {
            using var scope = ServiceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            var ytDownload = false;
            IStreamDownloader downloader = null;
            if (downloadClient.Url.StartsWith("https://streamtape.com"))
                downloader = new StreamTape();
            if (downloadClient.Url.StartsWith("https://voe.sx") || downloadClient.Url.StartsWith("https://yodelswartlike.com"))
                downloader = new Voe();
            if (downloader != null)
            {
                var downloadExtract = await downloader.GetDownloadLink(downloadClient);
                downloadClient.Url = downloadExtract.DownloadLink;
                ytDownload = true;
            }
            if (downloadClient.Url.Contains("m3u8") || ytDownload)
            {
                _ = YtDlpDownload(downloadClient);
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
                    var response = await BrowserAutomation.GetM3U8FromUrl(downloadClient.Url);
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
            }
        }

        public Task CancelDownload(int userId, string downloadId)
        {
            using (var scope = ServiceProvider.CreateScope())
            {
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var downloadClient = userDownloads.FirstOrDefault(x => x.UniqueId == downloadId);
                if (downloadClient == null || downloadClient.UserId != userId ||
                    downloadClient.CancellationToken.IsCancellationRequested) return Task.CompletedTask;
                downloadClient.StopKeepAlive();
                downloadClient.CancellationToken.Cancel();
            }

            return Task.CompletedTask;
        }

        private async Task StartNextYtDownload()
        {
            using var scope = ServiceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            if (userDownloads.Count > 0)
            {
                var cancelledDownloads = userDownloads.Where(x => x.CancellationToken.IsCancellationRequested).ToList();
                cancelledDownloads.ForEach(x => userDownloads.TryTake(out x));
                while (userDownloads.Count(x => x.Running) < General.MaxParallelYtDownloads &&
                       !userDownloads.All(x => x.Running))
                {
                    var nextDownload =
                        userDownloads.FirstOrDefault(x => !x.Running && !x.CancellationToken.IsCancellationRequested);
                    if (nextDownload == null) continue;
                    nextDownload.Running = true;
                    var clientResult = new DownloadInfo("Your download is starting, please wait...",
                        nextDownload.FileName, nextDownload.UniqueId);
                    await hub.Clients.Group(nextDownload.UserId.ToString()).downloadProgress(clientResult);
                    _ = RunYtDlpDownload(nextDownload);
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
                var dbUser = postgres.Users?.Include(x => x.RememberTokens).Where(x =>
                    x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == client.Token)).FirstOrDefault();
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
                await hub.Clients.Group(client.UserId.ToString()).dialog(new Dialog(AlertType.Danger)
                    { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                Console.WriteLine(ex.ToString());
            }

            await hub.Clients.Group(client.UserId.ToString()).downloadFinished(client.UniqueId);
        }

        private async void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            try
            {
                using var scope = ServiceProvider.CreateScope();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var id = userWebDownloads[sender as WebClient];
                var perc = Math.Max(0, e.BytesReceived / (double)e.TotalBytesToReceive * 100);
                var timeLeft = TimeSpan.FromMilliseconds(id.Stopwatch.ElapsedMilliseconds / perc * (100 - perc))
                    .ToString(@"HH\:mm\:ss");
                var result =
                    new DownloadInfo(
                        $"{Math.Round(e.BytesReceived / 1048576d, 2)}MB of {Math.Round(e.TotalBytesToReceive / 1048576d, 2)}MB - {timeLeft} remaining",
                        id.FileName, id.UniqueId) { Id = id.UniqueId, Progress = perc };
                await hub.Clients.Group(id.UserId.ToString()).downloadProgress(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}