using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using System;
using System.Collections.Generic;
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
        //private readonly IHubContext<ServerHub, IServerHub> _hub;
        //PostgresContext _postgres;
        IServiceProvider _serviceProvider;
        public Dictionary<WebClient, DownloadClientValue> userDownloads = new Dictionary<WebClient, DownloadClientValue>();
        public CancellationTokenSource conversionCancelToken = null;
        public string conversionId { get; set; } = "";
        public DataManager(IServiceProvider provider)
        {
            FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official).Wait();
            var path = FFmpeg.ExecutablesPath;
            if (path == null)
                FFmpeg.SetExecutablesPath(Directory.GetCurrentDirectory());
            _serviceProvider = provider;
            AddDefaultRooms();
        }

        public async void AddDownload(string url, string fileName, string connectionId, string token)
        {
            var uniqueId = connectionId;
            using (var scope = _serviceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                if (url.Contains("m3u8"))
                {
                    if (conversionCancelToken != null)
                    {
                        await _hub.Clients.Client(connectionId).dialog(new Dialog() { Header = "Error", Question = $"There is already a m3u8 conversion in progress", Answer1 = "Ok" });
                        return;
                    }
                    //var info = await FFmpeg.GetMediaInfo(url);
                    await _hub.Clients.Client(connectionId).downloadListen("m3u8" + connectionId);
                    RunM3U8Conversion(url, fileName, connectionId, token);
                    return;
                }
                var webClient = new WebClient();
                webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
                webClient.DownloadDataCompleted += WebClient_DownloadDataCompleted;
                webClient.Headers.Set("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:106.0) Gecko/20100101 Firefox/106.0");
                userDownloads.Add(webClient, new(fileName, connectionId, token, url));
                try
                {
                    using (var stream = webClient.OpenRead(url))
                    {
                        var totalDownload = Convert.ToInt64(webClient.ResponseHeaders["Content-Length"]);
                        var mb = ((double)totalDownload / 1024d / 1024d);
                        if (totalDownload <= 0)
                        {
                            await _hub.Clients.Client(connectionId).dialog(new Dialog() { Header = "Error", Question = $"Not allowed to download anything above 500mb file was {mb}mb", Answer1 = "Ok" });
                            return;
                        }
                        await _hub.Clients.Client(connectionId).downloadListen(connectionId);
                        webClient.DownloadDataAsync(new Uri(url));
                    }
                }
                catch (Exception ex)
                {
                    userDownloads.Remove(webClient);
                    webClient.Dispose();
                    await _hub.Clients.Client(connectionId).dialog(new Dialog() { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                    return;
                }
            }
            return;
        }

        public async void RunM3U8Conversion(string url, string fileName, string connectionId, string token)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                RememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
                if (Token == null)
                    return;
                if (dbUser.userprivileges < 3)
                {
                    await _hub.Clients.Client(connectionId).dialog(new Dialog() { Header = "Error", Question = "No privileges to convert m3u8", Answer1 = "Ok" });
                    return;
                }
                var info = new DownloadInfo($"00:00:00 of 00:00:00");
                info.Id = "m3u8" + connectionId;
                info.Progress = 0;
                await _hub.Clients.Client(connectionId).downloadProgress(info);
                var dbFile = new DbFile(fileName, ".mp4", dbUser);
                var filePath = $"{General.FilePath}/{dbFile.FileKey}.mp4".Replace('\\', '/');
                try
                {
                    var conversion = await FFmpeg.Conversions.FromSnippet.SaveM3U8Stream(new Uri(url), filePath);
                    conversion.OnProgress += Conversion_OnProgress;
                    conversionCancelToken = new CancellationTokenSource();
                    conversionId = connectionId;
                    var result = await conversion.Start(conversionCancelToken.Token);
                    if (!File.Exists(filePath))
                    {
                        await _hub.Clients.Client(connectionId).dialog(new Dialog() { Header = "Error", Question = "There has been an error trying to save the file", Answer1 = "Ok" });
                        return;
                    }
                    dbUser.Files.Add(dbFile);
                    await _postgres.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    CancelM3U8Conversion(connectionId);
                    await _hub.Clients.Client(connectionId).dialog(new Dialog() { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                }
                if (File.Exists(filePath) && dbUser.Files.FirstOrDefault(x => x.FileKey == dbFile.FileKey) == null)
                    File.Delete(filePath);
                conversionCancelToken?.Dispose();
                conversionCancelToken = null;
                await _hub.Clients.Client(connectionId).downloadFinished("m3u8" + conversionId);
            }
        }

        public void CancelM3U8Conversion(string connectionId)
        {
            if (conversionCancelToken != null && conversionId == connectionId)
            {
                conversionCancelToken?.Cancel();
                conversionCancelToken = null;
            }
        }

        private async void Conversion_OnProgress(object sender, Xabe.FFmpeg.Events.ConversionProgressEventArgs args)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var result = new DownloadInfo($"{args.Duration} of {args.TotalLength}");
                result.Id = "m3u8" + conversionId;
                result.Progress = args.Percent;
                await _hub.Clients.Client(conversionId).downloadProgress(result);
            }
        }

        private void WebClient_DownloadDataCompleted(object sender, DownloadDataCompletedEventArgs e)
        {
            var client = sender as WebClient;
            var id = userDownloads[client];
            userDownloads.Remove(client);
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
                    await _hub.Clients.Client(client.ConnectionId).dialog(new Dialog() { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
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
                    var id = userDownloads[sender as WebClient];
                    var perc = e.BytesReceived / (double)e.TotalBytesToReceive * 100d;
                    if (perc < 0)
                        perc = -1;
                    var result = new DownloadInfo($"{Math.Round(e.BytesReceived / 1024d / 1024d, 2)}MB of {Math.Round(e.TotalBytesToReceive / 1024d / 1024d, 2)}MB");
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
