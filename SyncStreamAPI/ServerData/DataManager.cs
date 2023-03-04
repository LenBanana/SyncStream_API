using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.PostgresModels;
using System;
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

namespace SyncStreamAPI.ServerData
{
    public class DataManager
    {
        public static List<Room> Rooms { get; set; } = new List<Room>();
        public List<LiveUser> LiveUsers { get; set; } = new List<LiveUser>();
        public static bool checking { get; set; } = false;
        public Dictionary<WebClient, DownloadClientValue> userWebDownloads { get; set; } = new Dictionary<WebClient, DownloadClientValue>();
        public List<DownloadClientValue> userM3U8Conversions { get; set; } = new List<DownloadClientValue>();
        IServiceProvider _serviceProvider { get; set; }
        IConfiguration Configuration { get; }
        int MaxParallelConversions { get; set; } = 4;
        public DataManager(IServiceProvider provider)
        {
            FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official).Wait();
            var path = FFmpeg.ExecutablesPath;
            if (path == null)
                FFmpeg.SetExecutablesPath(Directory.GetCurrentDirectory());
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                LinuxBash.Bash($"chmod +x /app/ffmpeg && chmod +x /app/ffprobe");
                LinuxBash.DownloadYtDlp().Wait();
                LinuxBash.Bash($"chmod +x /app/yt-dlp");
                LinuxBash.Bash($"alias yt-dlp='python3 /app/yt-dlp'");
                //if (getYtDl)
                //    LinuxBash.Bash($"chmod a+rx /usr/local/bin/youtube-dl");
            }
            _serviceProvider = provider;
            using (var scope = _serviceProvider.CreateScope())
            {
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                Configuration = config;
            }
            ReadSettings();
            AddDefaultRooms();
        }

        public async void AddDefaultRooms()
        {
            Rooms.Add(new Room("Dreckroom", "dreck", false, true));
            Rooms.Add(new Room("Randomkeller", "random", false, true));
            Rooms.Add(new Room("Guffelstübchen", "guffel", false, true));
            for (int i = 1; i < 5; i++)
                Rooms.Add(new Room($"Guest Room - {i}", $"guest{i}", true, false));

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                    await _postgres.Database.EnsureCreatedAsync();
                    if (await _postgres.Folders.CountAsync() == 0)
                    {
                        _postgres.Folders.Add(new DbFileFolder("Default"));
                        await _postgres.SaveChangesAsync();
                    }
                }
            }
            catch
            {

            }
        }

        public async void ReadSettings()
        {
            var section = Configuration.GetSection("MaxParallelConversions");
            MaxParallelConversions = Convert.ToInt32(section.Value);
        }

        public async void AddMember(int id, string connectionId)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                await _hub.Groups.AddToGroupAsync(connectionId, id.ToString());
            }
            var clientQueueIdx = userM3U8Conversions.FindIndex(x => x.UserId == id);
            if (clientQueueIdx >= 0)
                userM3U8Conversions[clientQueueIdx].ConnectionId = connectionId;
        }

        public async void YtDownload(DownloadClientValue downloadClient)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                    var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                    await _hub.Clients.Client(downloadClient.ConnectionId).downloadListen(downloadClient.UniqueId);
                    var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == downloadClient.Token)).FirstOrDefault();
                    var dbFile = new DbFile(downloadClient.FileName, ".mp4", dbUser);
                    var filePath = $"{General.FilePath}/{dbFile.FileKey}.mp4".Replace('\\', '/');
                    var ytdl = new YoutubeDL();
                    ytdl.YoutubeDLPath = "/app/yt-dlp";
                    ytdl.FFmpegPath = "/app/ffmpeg";
                    ytdl.OutputFolder = filePath;
                    Console.WriteLine($"Downloading {downloadClient.Url} to {filePath}");
                    var progress = new Progress<DownloadProgress>(async p =>
                    {
                        try
                        {
                            var perc = p.Progress * 100f;
                            var text = $"{perc}%";
                            if (downloadClient.Stopwatch != null)
                            {
                                var millis = downloadClient.Stopwatch.ElapsedMilliseconds;
                                var timeLeft = (double)millis / perc * (100 - perc);
                                timeLeft = timeLeft < 0 || timeLeft > TimeSpan.MaxValue.TotalMilliseconds ? 0 : timeLeft;
                                var timeString = TimeSpan.FromMilliseconds(timeLeft).ToString(@"hh\:mm\:ss");
                                text += $" - {timeString} remaining";
                            }
                            var result = new DownloadInfo(text, downloadClient.FileName, downloadClient.UniqueId);
                            result.Progress = perc;
                            await _hub.Clients.Group(downloadClient.UserId.ToString()).downloadProgress(result);
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }
                    });
                    var res = await ytdl.RunVideoDownload(downloadClient.Url, progress: progress, ct: downloadClient.CancellationToken.Token);
                    if (res.Success)
                    {
                        dbUser.Files.Add(dbFile);
                        await _postgres.SaveChangesAsync();
                        Console.WriteLine($"Saved {filePath} to DB");
                    }
                    else
                    {
                        Console.WriteLine($"Error downloading {downloadClient.Url}: {res.ErrorOutput.FirstOrDefault()}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async void AddDownload(DownloadClientValue downloadClient)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var _browser = scope.ServiceProvider.GetRequiredService<BrowserAutomation>();
                if (downloadClient.Url.Contains("m3u8"))
                {
                    userM3U8Conversions.Add(downloadClient);
                    SendStatusToM3U8Clients();
                    if (userM3U8Conversions.Count > MaxParallelConversions)
                        return;
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
                        var mb = ((double)totalDownload / 1024d / 1024d);
                        if (totalDownload <= 0)
                        {
                            if (_browser == null)
                            {
                                await _hub.Clients.Client(downloadClient.ConnectionId).dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = $"Not allowed to download anything above 500mb file was {mb}mb", Answer1 = "Ok" });
                                return;
                            }
                            var response = await _browser.GetM3U8FromUrl(downloadClient.Url);
                            if (response != null)
                                await _hub.Clients.Client(downloadClient.ConnectionId).browserResults(response.OutputUrls);
                            return;
                        }
                        await _hub.Clients.Client(downloadClient.ConnectionId).downloadListen(downloadClient.ConnectionId);
                        webClient.DownloadDataAsync(new Uri(downloadClient.Url));
                    }
                }
                catch (Exception ex)
                {
                    userWebDownloads.Remove(webClient);
                    webClient.Dispose();
                    await _hub.Clients.Client(downloadClient.ConnectionId).dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                    return;
                }
            }
            return;
        }

        public async void RunM3U8Conversion(DownloadClientValue downloadClient)
        {
            downloadClient.Stopwatch = Stopwatch.StartNew();
            downloadClient.Running = true;
            using (var scope = _serviceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                await _hub.Clients.Client(downloadClient.ConnectionId).downloadListen(downloadClient.UniqueId);
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == downloadClient.Token)).FirstOrDefault();
                var dbFile = new DbFile(downloadClient.FileName, ".mp4", dbUser);
                var filePath = $"{General.FilePath}/{dbFile.FileKey}.mp4".Replace('\\', '/');
                try
                {
                    if (dbUser == null)
                        throw new Exception($"Unable to find user");
                    var conv = await FFmpeg.Conversions.New().AddParameter($"ffmpeg -i \"{downloadClient.Url}\" -c copy \"{downloadClient.FileName}\".mp4").UseMultiThread(true).Start(downloadClient.CancellationToken.Token);
                    var conversion = (await FFmpeg.Conversions.FromSnippet.SaveM3U8Stream(new Uri(downloadClient.Url), filePath)).UseMultiThread(true).SetOverwriteOutput(true);
                    conversion.OnProgress += async (sender, args) =>
                    {
                        try
                        {
                            var text = $"{args.Duration}/{args.TotalLength}";
                            if (downloadClient.Stopwatch != null)
                            {
                                var millis = downloadClient.Stopwatch.ElapsedMilliseconds;
                                var timeLeft = (double)millis / args.Percent * (100 - args.Percent);
                                timeLeft = timeLeft < 0 || timeLeft > TimeSpan.MaxValue.TotalMilliseconds ? 0 : timeLeft;
                                var timeString = TimeSpan.FromMilliseconds(timeLeft).ToString(@"hh\:mm\:ss");
                                text += $" - {timeString} remaining";
                            }
                            var result = new DownloadInfo(text, downloadClient.FileName, downloadClient.UniqueId);
                            result.Progress = args.Percent;
                            await _hub.Clients.Group(downloadClient.UserId.ToString()).downloadProgress(result);
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }
                    };
                    if (downloadClient?.CancellationToken?.Token != null)
                        await conversion.UseMultiThread(true).SetPreset(downloadClient.Preset).Start(downloadClient.CancellationToken.Token);
                    else
                        throw new OperationCanceledException();
                    if (!File.Exists(filePath))
                    {
                        await _hub.Clients.Client(downloadClient.ConnectionId).dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "There has been an error trying to save the file", Answer1 = "Ok" });
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
                    await _hub.Clients.Client(downloadClient?.ConnectionId).dialog(new Dialog(AlertTypes.Danger) { Header = header, Question = $"{header} \n{msg}", Answer1 = "Ok" });
                }
                try
                {
                    if (File.Exists(filePath) && dbUser.Files.FirstOrDefault(x => x.FileKey == dbFile.FileKey) == null)
                        File.Delete(filePath);
                }
                catch { }
                await _hub.Clients.Client(downloadClient.ConnectionId).downloadFinished(downloadClient.UniqueId);
                if (userM3U8Conversions.Contains(downloadClient))
                    userM3U8Conversions.Remove(downloadClient);
                StartNextDownload();
            }
        }

        public async void SendStatusToM3U8Clients()
        {
            using (var scope = _serviceProvider.CreateScope())
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

        public void CancelM3U8Conversion(string downloadId)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var idx = userM3U8Conversions.FindIndex(x => x.UniqueId == downloadId);
                if (idx >= 0)
                {
                    userM3U8Conversions[idx].CancellationToken.Cancel();
                    userM3U8Conversions[idx].StopKeepAlive();
                    if (idx > 0)
                        userM3U8Conversions.RemoveAt(idx);
                }
            }
        }

        public async void StartNextDownload()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                if (userM3U8Conversions.Count > 0)
                {
                    SendStatusToM3U8Clients();
                    if (userM3U8Conversions.Where(x => x.Running).Count() < MaxParallelConversions)
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
            using (var scope = _serviceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                try
                {

                    var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == client.Token)).FirstOrDefault();
                    if (dbUser == null)
                        return;
                    DbRememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == client.Token);
                    if (Token == null)
                        return;
                    if (dbUser.userprivileges >= UserPrivileges.Administrator)
                    {
                        if (!Directory.Exists(General.FilePath))
                            Directory.CreateDirectory(General.FilePath);
                        var dbFile = new DbFile(client.FileName, $".{client.Url.Split('.').Last()}", dbUser);
                        var filePath = $"{General.FilePath}/{dbFile.FileKey}{dbFile.FileEnding}".Replace('\\', '/');
                        File.WriteAllBytes(filePath, file);
                        dbUser.Files.Add(dbFile);
                        await _postgres.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    await _hub.Clients.Client(client.ConnectionId).dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                }
                await _hub.Clients.Client(client.ConnectionId).downloadFinished(client.UniqueId);
            }
        }

        private async void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                    var id = userWebDownloads[sender as WebClient];
                    var perc = e.BytesReceived / (double)e.TotalBytesToReceive * 100d;
                    if (perc < 0)
                        perc = -1;
                    var millis = id.Stopwatch.ElapsedMilliseconds;
                    var timeLeft = (double)millis / perc * (100 - perc);
                    if (timeLeft < 0)
                        timeLeft = 0;
                    if (timeLeft > TimeSpan.MaxValue.TotalMilliseconds)
                        timeLeft = TimeSpan.MaxValue.TotalMilliseconds;
                    var timeString = TimeSpan.FromMilliseconds(timeLeft).ToString(@"HH\:mm\:ss");
                    var result = new DownloadInfo($"{Math.Round(e.BytesReceived / 1024d / 1024d, 2)}MB of {Math.Round(e.TotalBytesToReceive / 1024d / 1024d, 2)}MB - {timeString} remaining", id.FileName, id.UniqueId);
                    result.Id = id.UniqueId;
                    result.Progress = perc;
                    await _hub.Clients.Client(id.ConnectionId).downloadProgress(result);
                }
            }
            catch (Exception ex)
            {

            }
        }

        public static Room GetRoom(string UniqueId)
        {
            return Rooms.FirstOrDefault(x => x.uniqueId == UniqueId);
        }
        public static List<Room> GetRooms()
        {
            return Rooms;
        }

        public void AddToMemberCheck(Member member)
        {
            member.Kicked += Member_Kicked;
        }

        private async void Member_Kicked(Member e)
        {
            await KickMember(e);
        }

        public async Task KickMember(Member e)
        {
            if (e != null)
            {
                try
                {
                    int idx = Rooms.FindIndex(x => x.uniqueId == e.RoomId);
                    if (idx > -1)
                    {
                        Room room = Rooms[idx];
                        e.Kicked -= Member_Kicked;
                        if (!room.server.members.Contains(e))
                            return;
                        room.server.members.Remove(e);
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                            if (room.server.members.Count > 0)
                            {
                                var game = room.GallowGame;
                                if (e.ishost)
                                {
                                    room.server.members[0].ishost = true;
                                    await _hub.Clients.Client(room.server.members[0].ConnectionId).hostupdate(true);
                                }
                            }
                            await _hub.Clients.Group(room.uniqueId).userupdate(room.server.members?.Select(x => x.ToDTO()).ToList());
                            await _hub.Clients.All.getrooms(Rooms);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }
}
