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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

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
        public DataManager(IServiceProvider provider)
        {
            _serviceProvider = provider;
            AddDefaultRooms();
        }

        public async Task<string?> AddDownload(string url, string fileName, string connectionId, string token)
        {
            var uniqueId = connectionId;
            var webClient = new WebClient();
            webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
            webClient.DownloadDataCompleted += WebClient_DownloadDataCompleted;
            webClient.Headers.Set("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:106.0) Gecko/20100101 Firefox/106.0");
            userDownloads.Add(webClient, new(fileName, connectionId, token, url)); using (var scope = _serviceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                try
                {
                    using (var stream = webClient.OpenRead(url))
                    {
                        var totalDownload = Convert.ToInt64(webClient.ResponseHeaders["Content-Length"]);
                        var mb = ((double)totalDownload / 1024d / 1024d);
                        if (totalDownload <= 0 || mb > 500)
                        {
                            await _hub.Clients.Client(connectionId).dialog(new Dialog() { Header = "Error", Question = $"Not allowed to download anything above 500mb file was {mb}mb", Answer1 = "Ok" });
                            return null;
                        }
                        webClient.DownloadDataAsync(new Uri(url));
                    }
                }
                catch (Exception ex)
                {
                    userDownloads.Remove(webClient);
                    webClient.Dispose();
                    await _hub.Clients.Client(connectionId).dialog(new Dialog() { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
                    return null;
                }
            }
            return uniqueId;
        }

        private void WebClient_DownloadDataCompleted(object sender, DownloadDataCompletedEventArgs e)
        {
            var client = sender as WebClient;
            var id = userDownloads[client];
            userDownloads.Remove(client);
            SaveFileToFilesystem(id, e.Result);
        }

        public async void SaveFileToFilesystem(DownloadClientValue client, byte[] file)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();

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
                    File.WriteAllBytes($"{General.FilePath}\\{dbFile.FileKey}{dbFile.FileEnding}", file);
                    dbUser.Files.Add(dbFile);
                    await _postgres.SaveChangesAsync();
                    await _hub.Clients.Client(client.ConnectionId).downloadFinished(client.UniqueId);
                }
            }
        }

        private async void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var id = userDownloads[sender as WebClient];
                var perc = e.BytesReceived / (double)e.TotalBytesToReceive * 100d;
                if (perc < 0)
                    perc = -1;
                var result = new DownloadInfo();
                result.Id = id.UniqueId;
                result.Progress = perc;
                await _hub.Clients.Client(id.ConnectionId).downloadProgress(result);
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
