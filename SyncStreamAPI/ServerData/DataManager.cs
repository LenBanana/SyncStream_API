using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace SyncStreamAPI.ServerData
{
    public class DataManager
    {
        public static List<Room> Rooms { get; set; } = new List<Room>();
        public static bool checking { get; set; } = false;
        public Dictionary<WebClient, DownloadClientValue> userWebDownloads { get; set; } = new Dictionary<WebClient, DownloadClientValue>();
        public List<DownloadClientValue> userM3U8Conversions { get; set; } = new List<DownloadClientValue>();

        IServiceProvider _serviceProvider { get; set; }
        Dictionary<int, string> UserToMemberList { get; set; } = new Dictionary<int, string>();
        public DataManager(IServiceProvider provider)
        {
            FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official).Wait();
            var path = FFmpeg.ExecutablesPath;
            if (path == null)
                FFmpeg.SetExecutablesPath(Directory.GetCurrentDirectory());
            _serviceProvider = provider;
            AddDefaultRooms();
        }

        public void AddMember(int id, string connectionId)
        {
            if (!UserToMemberList.ContainsKey(id))
                UserToMemberList.Add(id, connectionId);
            else
                if (UserToMemberList[id] != connectionId)
                UserToMemberList[id] = connectionId;
            var clientQueueIdx = userM3U8Conversions.FindIndex(x => x.UserId == id);
            if (clientQueueIdx >= 0)
                userM3U8Conversions[clientQueueIdx].ConnectionId = connectionId;
        }

        public async void AddDownload(DownloadClientValue downloadClient)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                if (downloadClient.Url.Contains("m3u8"))
                {
                    var info = new DownloadInfo($"Please wait...", downloadClient.FileName, downloadClient.UniqueId);
                    info.Progress = 0;
                    await _hub.Clients.Client(downloadClient.ConnectionId).downloadProgress(info);
                    if (userM3U8Conversions.Count > 0)
                    {
                        userM3U8Conversions.Add(downloadClient);
                        await _hub.Clients.Client(downloadClient.ConnectionId).dialog(new Dialog(AlertTypes.Info) { Header = "Added to queue", Question = $"{downloadClient.FileName} has been added to the queue, current position {userM3U8Conversions.Count}", Answer1 = "Ok" });
                        return;
                    }
                    userM3U8Conversions.Add(downloadClient);
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
                            await _hub.Clients.Client(downloadClient.ConnectionId).dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = $"Not allowed to download anything above 500mb file was {mb}mb", Answer1 = "Ok" });
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
            using (var scope = _serviceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                downloadClient.Stopwatch = Stopwatch.StartNew();
                await _hub.Clients.Client(downloadClient.ConnectionId).downloadListen("m3u8" + downloadClient.ConnectionId);
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == downloadClient.Token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                RememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == downloadClient.Token);
                if (Token == null)
                    return;
                if (dbUser.userprivileges < 3)
                {
                    await _hub.Clients.Client(downloadClient.ConnectionId).dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "No privileges to convert m3u8", Answer1 = "Ok" });
                    return;
                }
                var dbFile = new DbFile(downloadClient.FileName, ".mp4", dbUser);
                var filePath = $"{General.FilePath}/{dbFile.FileKey}.mp4".Replace('\\', '/');
                try
                {
                    var conversion = await FFmpeg.Conversions.FromSnippet.SaveM3U8Stream(new Uri(downloadClient.Url), filePath);
                    conversion.OnProgress += Conversion_OnProgress;
                    if (downloadClient?.CancellationToken?.Token != null)
                        await conversion.Start(downloadClient.CancellationToken.Token);
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
                        if (downloadClient != null)
                            await _hub.Clients.Client(downloadClient.ConnectionId).downloadFinished("m3u8" + downloadClient.UniqueId);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    await _hub.Clients.Client(downloadClient.ConnectionId).dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                }
                if (File.Exists(filePath) && dbUser.Files.FirstOrDefault(x => x.FileKey == dbFile.FileKey) == null)
                    File.Delete(filePath);
                await _hub.Clients.Client(downloadClient.ConnectionId).downloadFinished("m3u8" + downloadClient.UniqueId);
                userM3U8Conversions.RemoveAt(0);
                if (userM3U8Conversions.Count > 0)
                    StartNextDownload();
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
                    for (int i = 0; i < userM3U8Conversions.Count; i++)
                    {
                        if (UserToMemberList.ContainsKey(userM3U8Conversions[i].UserId))
                        {
                            var clientResult = new DownloadInfo($"Starting next download...", userM3U8Conversions[i].FileName, userM3U8Conversions[i].UniqueId);
                            await _hub.Clients.Client(UserToMemberList[userM3U8Conversions[i].UserId]).downloadProgress(clientResult);
                        }
                    }
                    var nextDownload = userM3U8Conversions[0];
                    await _hub.Clients.Client(nextDownload.ConnectionId).dialog(new Dialog(AlertTypes.Info) { Header = "Download startet", Question = $"Download {nextDownload.FileName} starting now", Answer1 = "Ok" });
                    RunM3U8Conversion(nextDownload);
                }
            }
        }

        private async void Conversion_OnProgress(object sender, Xabe.FFmpeg.Events.ConversionProgressEventArgs args)
        {
            try
            {
                if (UserToMemberList.ContainsKey(userM3U8Conversions[0].UserId))
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                        var text = $"{args.Duration}/{args.TotalLength}";
                        if (userM3U8Conversions[0].Stopwatch != null)
                        {
                            var millis = userM3U8Conversions[0].Stopwatch.ElapsedMilliseconds;
                            var timeLeft = (double)millis / args.Percent * (100 - args.Percent);
                            if (timeLeft < 0)
                                timeLeft = 0;
                            if (timeLeft > TimeSpan.MaxValue.TotalMilliseconds)
                                timeLeft = TimeSpan.MaxValue.TotalMilliseconds;
                            var timeString = TimeSpan.FromMilliseconds(timeLeft).ToString(@"hh\:mm\:ss");
                            text += $" - {timeString} remaining";
                        }
                        var result = new DownloadInfo(text, userM3U8Conversions[0].FileName, userM3U8Conversions[0].UniqueId);
                        result.Progress = args.Percent;
                        await _hub.Clients.Client(UserToMemberList[userM3U8Conversions[0].UserId]).downloadProgress(result);
                        for (int i = 1; i < userM3U8Conversions.Count; i++)
                        {
                            if (UserToMemberList.ContainsKey(userM3U8Conversions[i].UserId))
                            {
                                var clientResult = new DownloadInfo($"{i} download{((i) > 1 ? "s" : "")} infront of you", userM3U8Conversions[i].FileName, userM3U8Conversions[i].UniqueId);
                                clientResult.Progress = args.Percent;
                                await _hub.Clients.Client(UserToMemberList[userM3U8Conversions[i].UserId]).downloadProgress(clientResult);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
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
                    RememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == client.Token);
                    if (Token == null)
                        return;
                    if (dbUser.userprivileges >= 3)
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

        public void AddDefaultRooms()
        {
            Rooms.Add(new Room("Dreckroom", "dreck", false, true));
            Rooms.Add(new Room("Randomkeller", "random", false, true));
            Rooms.Add(new Room("BigWeinerClub", "weiner", false, true));
            for (int i = 1; i < 5; i++)
                Rooms.Add(new Room($"Guest Room - {i}", $"guest{i}", true, false));
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
