using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.ServerData.Helper
{
    public class RoomManager
    {
        IServiceProvider _serviceProvider { get; set; }
        public BlockingCollection<Room> Rooms { get; set; } = new BlockingCollection<Room>();
        public RoomManager(IServiceProvider serviceProvider, BlockingCollection<Room> rooms)
        {
            _serviceProvider = serviceProvider;
            Rooms = rooms;
        }
        public Room GetRoom(string UniqueId)
        {
            return Rooms.FirstOrDefault(x => x.uniqueId == UniqueId);
        }
        public BlockingCollection<Room> GetRooms()
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
                    var room = Rooms.FirstOrDefault(x => x.uniqueId == e.RoomId);
                    if (room != null)
                    {                        
                        e.Kicked -= Member_Kicked;
                        if (!room.server.members.Contains(e))
                        {
                            return;
                        }

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
