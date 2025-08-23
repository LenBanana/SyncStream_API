using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScreenIT.Helper;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Helper.StreamingSites;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.Interfaces;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData.Helper;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using YoutubeDLSharp;

namespace SyncStreamAPI.ServerData;

public class MainManager
{
    public MainManager(IServiceProvider provider)
    {
        FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official).Wait();
        var path = FFmpeg.ExecutablesPath;
        if (path == null) FFmpeg.SetExecutablesPath(Directory.GetCurrentDirectory());

        LinuxBash.DownloadYtDlp().Wait();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            LinuxBash.Bash("chmod +x /app/ffmpeg && chmod +x /app/ffprobe");
            LinuxBash.Bash("chmod +x /app/yt-dlp");
            LinuxBash.Bash("alias yt-dlp='python3 /app/yt-dlp'");
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

    public static BlockingCollection<Room> Rooms { get; set; } = new();
    public static GeneralManager GeneralManager { get; set; }
    public static RoomManager RoomManager { get; set; }
    public BlockingCollection<LiveUser> LiveUsers { get; set; } = new();

    public Dictionary<WebClient, DownloadClientValue> userWebDownloads { get; set; } = new();

    public Dictionary<CancellationTokenSource, BlockingCollection<DownloadClientValue>>
        userYtPlaylistDownload { get; set; } =
        new();

    public BlockingCollection<DownloadClientValue> userDownloads { get; set; } = new();

    public static IServiceProvider ServiceProvider { get; set; }
    private IConfiguration Configuration { get; }

    public static GeneralManager GetGeneralManager()
    {
        return GeneralManager;
    }

    public static RoomManager GetRoomManager()
    {
        return RoomManager;
    }

    public static Room? GetRoom(string UniqueId)
    {
        return RoomManager.GetRoom(UniqueId);
    }

    public static IEnumerable<Room> GetRooms()
    {
        return RoomManager.GetRooms();
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

    public async Task SendDefaultDialog(string group, string message, AlertType alertType, string header = "")
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
        foreach (var vid in vids) _ = YtDlpDownload(vid, linkedTokens);
    }

    [ErrorHandling]
    public async Task YtDlpDownload(DownloadClientValue downloadClient, CancellationTokenSource tokenSource = null)
    {
        userDownloads.Add(downloadClient);
        if (userDownloads.Count > General.MaxParallelYtDownloads) return;

        if (downloadClient.CancellationToken.IsCancellationRequested &&
            userYtPlaylistDownload.TryGetValue(tokenSource, out var downloads))
        {
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
        dbFile.GetPath();
        var ytdl = General.GetYoutubeDl();
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
                progressTimer?.DisposeAsync();
                progressTimer = null;
            }, null, TimeSpan.FromSeconds(1), TimeSpan.Zero);
        });
        downloadClient.Stopwatch = Stopwatch.StartNew();
        RunResult<string> runResult = null;
        await hub.Clients.Group(userId).downloadListen(downloadClient.UniqueId);
        try
        {
            runResult = await YtDLPHelper.DownloadMedia(ytdl, downloadClient, downloadClient.AudioOnly, progress);
            if (runResult is { Success: true })
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

    public Task CancelDownload(int userId, string downloadId)
    {
        using (var scope = ServiceProvider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
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
                    userDownloads.FirstOrDefault(x => x is
                        { Running: false, CancellationToken.IsCancellationRequested: false });
                if (nextDownload == null) continue;
                nextDownload.Running = true;
                var clientResult = new DownloadInfo("Your download is starting, please wait...",
                    nextDownload.FileName, nextDownload.UniqueId);
                await hub.Clients.Group(nextDownload.UserId.ToString()).downloadProgress(clientResult);
                _ = RunYtDlpDownload(nextDownload);
            }
        }
    }

    public async Task AddDownload(DownloadClientValue downloadClient)
    {
        using var scope = ServiceProvider.CreateScope();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
        var ytDownload = false;
        IStreamDownloader? downloader = null;

        // Existing downloader logic preserved
        if (downloadClient.Url.StartsWith("https://streamtape.com"))
            downloader = new StreamTape();
        if (downloadClient.Url.StartsWith("https://voe.sx") ||
            downloadClient.Url.StartsWith("https://yodelswartlike.com"))
            downloader = new Voe();
        if (downloader != null)
        {
            var downloadExtract = await downloader.GetDownloadLink(downloadClient);
            downloadClient.Url = downloadExtract.DownloadLink;
            ytDownload = true;
        }

        // Existing m3u8/ytDownload logic preserved
        if (downloadClient.Url.Contains("m3u8") || ytDownload)
        {
            _ = YtDlpDownload(downloadClient);
            return;
        }

        // REPLACED: WebClient with HttpClient for streaming
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:106.0) Gecko/20100101 Firefox/106.0");

            using var response =
                await httpClient.GetAsync(downloadClient.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;

            // Existing zero-length handling preserved
            if (totalBytes <= 0)
            {
                var browserResponse = await BrowserAutomation.GetM3U8FromUrl(downloadClient.Url);
                if (browserResponse != null)
                    await hub.Clients.Group(downloadClient.UserId.ToString())
                        .browserResults(browserResponse.OutputUrls);
                return;
            }

            // NEW: Stream the download instead of loading into memory
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await SaveFileToFilesystemStreaming(downloadClient, contentStream, totalBytes);
        }
        catch (Exception ex)
        {
            // Existing error handling preserved
            await hub.Clients.Group(downloadClient.UserId.ToString()).dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
            Console.WriteLine(ex.ToString());
        }
    }

// NEW streaming method with progress tracking (equivalent to WebClient events)
    private async Task SaveFileToFilesystemStreaming(DownloadClientValue client, Stream inputStream, long totalBytes)
    {
        using var scope = ServiceProvider.CreateScope();
        var postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();

        try
        {
            // All existing database/authentication logic preserved
            var dbUser = (postgres.Users.Include(x => x.RememberTokens))
                .FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == client.Token));

            var token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == client.Token);
            if (token == null) return;

            if (dbUser is { userprivileges: >= UserPrivileges.Administrator })
            {
                // Existing directory creation logic preserved
                if (!Directory.Exists(General.FilePath))
                    Directory.CreateDirectory(General.FilePath);

                // Existing file path logic preserved
                var dbFile = new DbFile(client.FileName, $".{client.Url.Split('.').Last()}", dbUser);
                var filePath = dbFile.GetPath();

                // REPLACED: File.WriteAllBytesAsync with streaming + progress tracking
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                var buffer = new byte[81920]; // 80KB buffer for better performance
                long totalBytesRead = 0;
                int bytesRead;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew(); // Start timing like your original code

                while ((bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    // Progress tracking equivalent to WebClient_DownloadProgressChanged
                    await ReportDownloadProgress(hub, client, totalBytesRead, totalBytes, stopwatch);
                }

                stopwatch.Stop();

                // Existing database save logic preserved
                dbUser.Files.Add(dbFile);
                await postgres.SaveChangesAsync();

                // Completion equivalent to WebClient_DownloadDataCompleted
                await ReportDownloadCompleted(hub, client);
            }
        }
        catch (Exception ex)
        {
            // Existing error handling preserved
            await hub.Clients.Group(client.UserId.ToString()).dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
            Console.WriteLine(ex.ToString());
        }
    }

// Equivalent to WebClient_DownloadProgressChanged event handler
    private async Task ReportDownloadProgress(IHubContext<ServerHub, IServerHub> hub, DownloadClientValue client,
        long bytesReceived, long totalBytesToReceive, System.Diagnostics.Stopwatch stopwatch)
    {
        try
        {
            var perc = Math.Max(0, bytesReceived / (double)totalBytesToReceive * 100);
            var timeLeft = TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds / perc * (100 - perc))
                .ToString(@"HH\:mm\:ss");
            var result = new DownloadInfo(
                $"{Math.Round(bytesReceived / 1048576d, 2)}MB of {Math.Round(totalBytesToReceive / 1048576d, 2)}MB - {timeLeft} remaining",
                client.FileName, client.UniqueId) { Id = client.UniqueId, Progress = perc };
            await hub.Clients.Group(client.UserId.ToString()).downloadProgress(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

// Equivalent to WebClient_DownloadDataCompleted event handler
    private async Task ReportDownloadCompleted(IHubContext<ServerHub, IServerHub> hub, DownloadClientValue client)
    {
        await hub.Clients.Group(client.UserId.ToString()).downloadFinished(client.UniqueId);
    }

// OPTIONAL: Keep your original method if other parts of your code still call it
// This version now also uses streaming internally
    private async Task SaveFileToFilesystem(DownloadClientValue client, byte[] file)
    {
        using var memoryStream = new MemoryStream(file);
        await SaveFileToFilesystemStreaming(client, memoryStream, file.Length);
    }
}