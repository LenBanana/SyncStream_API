﻿using HtmlAgilityPack;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.ServerData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    [EnableCors("CORSPolicy")]
    public partial class ServerHub : Hub<IServerHub>
    {
        IConfiguration Configuration { get; }

        DataManager _manager;

        MariaContext _maria;

        public ServerHub(IConfiguration configuration, DataManager manager, MariaContext maria)
        {
            Configuration = configuration;
            _manager = manager;
            _maria = maria;
        }

#nullable enable
        public override Task OnDisconnectedAsync(Exception? ex)
        {
            var Rooms = DataManager.GetRooms();
            int idx = Rooms.FindIndex(x => x.server.members.FirstOrDefault(y => y.ConnectionId == Context.ConnectionId) != null);
            if (idx > -1)
            {
                Room room = Rooms[idx];
                Member e = room.server.members.First(x => x.ConnectionId == Context.ConnectionId);
                e.InvokeKick();
            }
            return base.OnDisconnectedAsync(ex);
        }
#nullable disable

        public async Task SendServer(Server server, string UniqueId)
        {
            await Clients.Group(UniqueId).sendserver(server);
        }

        private Room GetRoom(string UniqueId)
        {
            List<Room> Rooms = DataManager.GetRooms();
            Room room = Rooms.FirstOrDefault(x => x.uniqueId == UniqueId);
            if (room == null)
                return null;
            return room;
        }

        public async Task AddVideo(DreckVideo key, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (!room.server.playlist.Select(x => x.url).Contains(key.url))
            {
                if (!key.url.Contains("youtube") && !key.url.Contains("youtu.be")
                    && !key.url.Contains("twitch.tv"))
                {
                    if (key.title.Length == 0)
                        key.title = "External Source";
                    if (key.title == "Vimeo - Video")
                    {
                        VimeoResponse vimeo = Vimeo.FromUrl(key.url);
                        if (vimeo != null)
                        {
                            key.title = vimeo?.Title + (vimeo.UserName == null ? "" : " - " + vimeo.UserName);
                        }
                    }
                }
                else
                {
                    if (key.url.Split('?')[0].ToLower().EndsWith("playlist"))
                    {
                        await AddPlaylist(key.url, UniqueId);
                        return;
                    }
                    if (key.title == null || key.title.Length == 0)
                        key.title = await General.ResolveURL(key.url, Configuration);
                }
                room.server.playlist.Add(key);
            }
            if (room.server.currentVideo.url.Length == 0 || room.server.currentVideo.ended == true)
            {
                room.server.currentVideo = key;
                await Clients.Group(UniqueId).videoupdate(key);
            }
            await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task AddPlaylist(string url, string UniqueId)
        {
            try
            {
                string title, source;
                (title, source) = await General.ResolveTitle(url, 8);
                if (source.Length > 0)
                {
                    HtmlDocument doc = new HtmlDocument();
                    doc.LoadHtml(source);
                    HtmlNode vids = doc.DocumentNode.ChildNodes[1].ChildNodes[1].ChildNodes[15];
                    string json = vids.InnerText.Split(new[] { '=' }, 2)[1].Split(new[] { ';' }, 2)[0];
                    PlaylistInfo playlistInfo = new PlaylistInfo().FromJson(json);
                    var playlist = playlistInfo.Contents.TwoColumnBrowseResultsRenderer.Tabs[0].TabRenderer.Content.SectionListRenderer.Contents[0].ItemSectionRenderer.Contents[0].PlaylistVideoListRenderer.Contents;
                    foreach (var video in playlist)
                    {
                        DreckVideo vid = new DreckVideo();
                        vid.ended = false;
                        vid.title = video.PlaylistVideoRenderer?.Title.Runs[0].Text;
                        vid.url = "https://www.youtube.com/watch?v=" + video.PlaylistVideoRenderer?.VideoId;
                        await AddVideo(vid, UniqueId).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                await Clients.Caller.dialog(new Dialog() { Header = "Error", Question = "There has been an error trying to add the playlist", Answer1 = "Ok" });
            }
        }

        public async Task RemoveVideo(string key, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            int idx = room.server.playlist.FindIndex(x => x.url == key);
            if (idx != -1)
            {
                room.server.playlist.RemoveAt(idx);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(DataManager.GetRooms());
            }
        }

        public async Task NextVideo(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            if (room.server.playlist.Count > 1)
            {
                MainServer.currentVideo = room.server.playlist[1];
                room.server.playlist.RemoveAt(0);
                await Clients.Group(UniqueId).videoupdate(MainServer.currentVideo);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(DataManager.GetRooms());
                return;
            }
            else if (room.server.playlist.Count == 1)
            {
                MainServer.currentVideo.ended = true;
                room.server.playlist.RemoveAt(0);
                room.server.isplaying = false;
                await Clients.Group(UniqueId).isplayingupdate(room.server.isplaying);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.Group(UniqueId).sendserver(room.server);
                await Clients.All.getrooms(DataManager.GetRooms());
            }
        }

        public async Task PlayVideo(string key, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            int idx = MainServer.playlist.FindIndex(x => x.url == key);
            if (idx != -1)
            {
                DreckVideo tempUrl = room.server.playlist[idx];
                room.server.playlist.RemoveAt(idx);
                room.server.playlist.RemoveAt(0);
                room.server.playlist.Insert(0, tempUrl);
                MainServer.currentVideo = room.server.playlist[0];
                await Clients.Group(UniqueId).videoupdate(MainServer.currentVideo);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(DataManager.GetRooms());
                return;
            }
        }

        public async Task MoveVideo(int fromIndex, int toIndex, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            DreckVideo vid = room.server.playlist[fromIndex];
            room.server.playlist.RemoveAt(fromIndex);
            room.server.playlist.Insert(toIndex, vid);
            await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
        }

        public async Task SetTime(double time, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (room.server.currentVideo.url.Contains("twitch.tv"))
                await Clients.Group(UniqueId).twitchTimeUpdate(time);
            else
                await Clients.Group(UniqueId).timeupdate(time);
            room.server.currenttime = time;
        }

        public async Task PlayPause(bool isplaying, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            room.server.isplaying = isplaying;
            if (room.server.currentVideo.url.Contains("twitch.tv"))
                await Clients.Group(UniqueId).twitchPlaying(isplaying);
            else
                await Clients.Group(UniqueId).isplayingupdate(isplaying);

            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task UpdateUser(string username, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            int idx = MainServer.members.FindIndex(x => x.username == username);
            if (idx != -1)
            {
                //var address = Context.GetHttpContext().Connection.RemoteIpAddress;
                //var ip = address.ToString();
                var ip = Context.ConnectionId;
                MainServer.members[idx].ConnectionId = ip;
                if (MainServer.bannedMembers.Any(x => x.ConnectionId == MainServer.members[idx].ConnectionId))
                {
                    MainServer.members.RemoveAt(idx);
                    await Clients.Caller.adduserupdate((int)UserUpdate.Banned);
                    await Clients.Group(UniqueId).userupdate(MainServer.members);
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, UniqueId);
                    return;
                }
                MainServer.members[idx].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                if (MainServer.members.Count == 1 && !MainServer.members[idx].ishost)
                {
                    MainServer.members[idx].ishost = true;
                    await Clients.Client(MainServer.members[idx].ConnectionId).hostupdate(true);
                    await Clients.Group(UniqueId).userupdate(MainServer.members);
                }
            }
            else
            {
                await AddUser(username, UniqueId, "");
                return;
            }
        }

        public async Task ChangeHost(string usernameMember, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            int idxHost = MainServer.members.FindIndex(x => x.ishost == true);
            int idxMember = MainServer.members.FindIndex(x => x.username == usernameMember);
            if (idxHost != -1 && idxMember != -1)
            {
                MainServer.members[idxHost].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                MainServer.members[idxMember].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                MainServer.members[idxHost].ishost = false;
                MainServer.members[idxMember].ishost = true;
                await Clients.Client(MainServer.members[idxHost].ConnectionId).hostupdate(false);
                await Clients.Client(MainServer.members[idxMember].ConnectionId).hostupdate(true);
                await Clients.Group(UniqueId).userupdate(MainServer.members);
            }
        }

        public async Task AddRoom(Room room)
        {
            var Rooms = DataManager.GetRooms();
            int RoomCount = 0;
            while (Rooms.Any(x => x.uniqueId == room.uniqueId))
                room.uniqueId = room.uniqueId + RoomCount++;
            Rooms.Add(room);
            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task RemoveRoom(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            DataManager.GetRooms().Remove(room);
            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task AddUser(string username, string UniqueId, string password)
        {
            var ip = Context.ConnectionId;
            Console.WriteLine(ip);
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (room.password != null && room.password != password)
            {
                await Clients.Caller.adduserupdate((int)UserUpdate.WrongPassword);
                return;
            }
            Server MainServer = room.server;
            if (MainServer.members == null)
            {
                MainServer.members = new List<Member>();
            }
            Member newMember = new Member() { username = username, ishost = MainServer.members.Count == 0 ? true : false, ConnectionId = ip, RoomId = UniqueId };
            _manager.AddToMemberCheck(newMember);
            if (MainServer.bannedMembers.Any(x => x.ConnectionId == newMember.ConnectionId))
            {
                await Clients.Caller.adduserupdate((int)UserUpdate.Banned);
                return;
            }
            if (MainServer.chatmessages == null)
                MainServer.chatmessages = new List<ChatMessage>();
            await Groups.AddToGroupAsync(Context.ConnectionId, UniqueId);
            MainServer.members.Add(newMember);
            await Clients.Group(UniqueId).userupdate(MainServer.members);
            await Clients.Caller.isplayingupdate(MainServer.isplaying);
            await Clients.Caller.hostupdate(newMember.ishost);
            await Clients.All.getrooms(DataManager.GetRooms());
            await Clients.Caller.adduserupdate((int)UserUpdate.Success);
            if (MainServer.playlist.Count > 0)
                await Clients.Caller.playlistupdate(MainServer.playlist);
        }

        public async Task RemoveUser(string username, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            int idx = MainServer.members.FindIndex(x => x.username == username);
            if (idx == -1)
                return;
            bool isHost = MainServer.members[idx].ishost;
            MainServer.members.RemoveAt(idx);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, UniqueId);
            if (isHost && MainServer.members.Count > 0)
            {
                MainServer.members[0].ishost = true;
                await Clients.Client(MainServer.members[0].ConnectionId).hostupdate(true);
            }
            await Clients.Group(UniqueId).userupdate(MainServer.members);
            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task BanUser(string username, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            var member = MainServer.members.FirstOrDefault(x => x.username == username);
            if (member != null)
            {
                MainServer.members.Remove(member);
                MainServer.bannedMembers.Add(member);
            }
            await Clients.Group(UniqueId).userupdate(MainServer.members);
            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task SendMessage(ChatMessage message, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            message.time = DateTime.Now;
            MainServer.chatmessages.Add(message);
            if (MainServer.chatmessages.Count >= 100)
                MainServer.chatmessages = MainServer.chatmessages.GetRange(MainServer.chatmessages.Count - 100, MainServer.chatmessages.Count);
            await Clients.Group(UniqueId).sendmessage(MainServer.chatmessages);
        }

        public async Task SendPrivateMessage(string UniqueId, string FromUser, string ToUser, string Message)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                Server MainServer = room.server;
                Member FromIdx = MainServer.members.FirstOrDefault(x => x.username == FromUser);
                Member ToIdx = MainServer.members.FirstOrDefault(x => x.username == ToUser);
                if (FromIdx != null && ToIdx != null)
                {
                    string FullMessage = FromIdx.AddMessage(ToUser, Message);
                    await Clients.Caller.PrivateMessage(FullMessage);
                    await Clients.Client(ToIdx.ConnectionId).PrivateMessage(FullMessage);
                }
                else
                {
                    throw new Exception("User was not found");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                await Clients.Caller.dialog(new Dialog() { Header = "Message Error", Question = "There has been an error trying to send your message, please try again.", Answer1 = "Ok" });
            }
        }

        public async Task Ping(DateTime date)
        {
            await Clients.Caller.PingTest(date);
        }
    }
}
