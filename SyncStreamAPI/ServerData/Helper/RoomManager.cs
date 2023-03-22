using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.ServerData.Helper
{
    public class RoomManager
    {
        IServiceProvider _serviceProvider { get; set; }
        public List<Room> Rooms { get; set; } = new List<Room>();
        public RoomManager(IServiceProvider serviceProvider, List<Room> rooms)
        {
            _serviceProvider = serviceProvider;
            Rooms = rooms;
        }
        public Room GetRoom(string UniqueId)
        {
            return Rooms.FirstOrDefault(x => x.uniqueId == UniqueId);
        }
        public List<Room> GetRooms()
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
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        public async void AddMember(int id, string connectionId)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                await _hub.Groups.AddToGroupAsync(connectionId, id.ToString());
            }
        }
    }
}
