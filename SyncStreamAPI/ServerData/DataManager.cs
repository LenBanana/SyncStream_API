using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SyncStreamAPI.ServerData
{
    public class DataManager
    {
        public static List<Room> Rooms = new List<Room>();
        public static bool checking = false;
        private readonly IHubContext<ServerHub, IServerHub> _hub;

        public DataManager(IHubContext<ServerHub, IServerHub> hub)
        {
            _hub = hub;
            AddDefaultRooms();
        }

        public void AddDefaultRooms()
        {
            Rooms.Add(new Room() { name = "Dreckroom", server = new Server(), uniqueId = "dreck" });
            Rooms.Add(new Room() { name = "Randomkeller", server = new Server(), uniqueId = "random" });
            Rooms.Add(new Room() { name = "GuestRoom 1", server = new Server(), uniqueId = "guest1" });
            Rooms.Add(new Room() { name = "GuestRoom 2", server = new Server(), uniqueId = "guest2" });
            Rooms.Add(new Room() { name = "GuestRoom 3", server = new Server(), uniqueId = "guest3" });
            Rooms.Add(new Room() { name = "GuestRoom 4", server = new Server(), uniqueId = "guest4" });
            Rooms.Add(new Room() { name = "MovieRoom 1", server = new Server(), uniqueId = "movie1" });
            Rooms.Add(new Room() { name = "MovieRoom 2", server = new Server(), uniqueId = "movie2" });
            Rooms.Add(new Room() { name = "MovieRoom 3", server = new Server(), uniqueId = "movie3" });
            Rooms.Add(new Room() { name = "MovieRoom 4", server = new Server(), uniqueId = "movie4" });
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

                        if (room.server.members.Count > 0)
                        {
                            var game = room.GallowGame;
                            if (e.ishost)
                            {
                                room.server.members[0].ishost = true;
                                await _hub.Clients.Client(room.server.members[0].ConnectionId).hostupdate(true);
                            }
                        }
                        await _hub.Clients.Group(room.uniqueId).userupdate(room.server.members.Select(x => x.ToDTO()).ToList());
                        await _hub.Clients.All.getrooms(Rooms);
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
