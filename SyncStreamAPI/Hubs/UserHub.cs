using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
using SyncStreamAPI.ServerData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {

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
            Member newMember = new Member(_manager) { username = username, ishost = MainServer.members.Count == 0 ? true : false, ConnectionId = ip, RoomId = UniqueId };
            if (MainServer.bannedMembers.Any(x => x.ConnectionId == newMember.ConnectionId))
            {
                await Clients.Caller.adduserupdate((int)UserUpdate.Banned);
                return;
            }
            if (MainServer.chatmessages == null)
                MainServer.chatmessages = new List<ChatMessage>();
            await Groups.AddToGroupAsync(Context.ConnectionId, UniqueId);
            MainServer.members.Add(newMember);
            if (newMember.ishost)
                MainServer.PlayingGallows = false;

            await Clients.Group(UniqueId).userupdate(MainServer.members.Select(x => x.ToDTO()).ToList());
            if (room.server.PlayingGallows)
            {
                await Clients.Caller.playinggallows(room.server.GallowWord);
                await Clients.Caller.gallowusers(room.server.members.Select(x => x.ToDTO()).ToList());
            }
            await Clients.Caller.isplayingupdate(MainServer.isplaying);
            await Clients.Caller.hostupdate(newMember.ishost);
            await Clients.All.getrooms(DataManager.GetRooms());
            await Clients.Caller.adduserupdate((int)UserUpdate.Success);
            if (MainServer.playlist.Count > 0)
                await Clients.Caller.playlistupdate(MainServer.playlist);
        }

        public async Task UpdateUser(string username, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            int idx = MainServer.members.FindIndex(x => x.ConnectionId == Context.ConnectionId);
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
                    await Clients.Group(UniqueId).userupdate(MainServer.members.Select(x => x.ToDTO()).ToList());
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, UniqueId);
                    return;
                }
                MainServer.members[idx].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                if (MainServer.members.Count == 1)
                {
                    if (!MainServer.members[idx].ishost)
                    {
                        MainServer.members[idx].ishost = true;
                        await Clients.Client(MainServer.members[idx].ConnectionId).hostupdate(true);
                        await Clients.Group(UniqueId).userupdate(MainServer.members.Select(x => x.ToDTO()).ToList());
                    }
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
            int idxMember = MainServer.members.FindIndex(x => x.username == usernameMember || x.ConnectionId == usernameMember);
            if (idxHost != -1 && idxMember != -1)
            {
                if (MainServer.PlayingGallows)
                    MainServer.UpdateGallowWord(true);
                MainServer.members[idxHost].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                MainServer.members[idxMember].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                MainServer.members[idxHost].ishost = false;
                MainServer.members[idxMember].ishost = true;
                await Clients.Client(MainServer.members[idxHost].ConnectionId).hostupdate(false);
                await Clients.Client(MainServer.members[idxMember].ConnectionId).hostupdate(true);
                await Clients.Group(UniqueId).userupdate(MainServer.members.Select(x => x.ToDTO()).ToList());
            }
        }

        public async Task RemoveUser(string username, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            int idx = MainServer.members.FindIndex(x => x != null && x.username == username);
            if (idx == -1)
                return;
            bool isHost = MainServer.members[idx].ishost;
            MainServer.members.RemoveAt(idx);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, UniqueId);
            if (MainServer.members.Count > 0)
            {
                if (MainServer.PlayingGallows)
                    if (MainServer.members.Count == 1)
                        await PlayGallows(UniqueId);

                if (isHost)
                {
                    if (MainServer.PlayingGallows)
                        MainServer.UpdateGallowWord(true);
                    MainServer.members[0].ishost = true;
                    await Clients.Client(MainServer.members[0].ConnectionId).hostupdate(true);
                }
            }
            await Clients.Group(UniqueId).userupdate(MainServer.members.Select(x => x.ToDTO()).ToList());
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
            await Clients.Group(UniqueId).userupdate(MainServer.members.Select(x => x.ToDTO()).ToList());
            await Clients.All.getrooms(DataManager.GetRooms());
        }
    }
}
