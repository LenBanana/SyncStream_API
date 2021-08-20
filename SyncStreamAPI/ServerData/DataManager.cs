using Microsoft.AspNetCore.SignalR;
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
            Rooms.Add(new Room() { name = "Dreckroom", server = new Server(this), uniqueId = "dreck" });
            Rooms.Add(new Room() { name = "Randomkeller", server = new Server(this), uniqueId = "random" });
            Rooms.Add(new Room() { name = "GuestRoom 1", server = new Server(this), uniqueId = "guest1" });
            Rooms.Add(new Room() { name = "GuestRoom 2", server = new Server(this), uniqueId = "guest2" });
            Rooms.Add(new Room() { name = "GuestRoom 3", server = new Server(this), uniqueId = "guest3" });
            Rooms.Add(new Room() { name = "GuestRoom 4", server = new Server(this), uniqueId = "guest4" });
            Rooms.Add(new Room() { name = "MovieRoom 1", server = new Server(this), uniqueId = "movie1" });
            Rooms.Add(new Room() { name = "MovieRoom 2", server = new Server(this), uniqueId = "movie2" });
            Rooms.Add(new Room() { name = "MovieRoom 3", server = new Server(this), uniqueId = "movie3" });
            Rooms.Add(new Room() { name = "MovieRoom 4", server = new Server(this), uniqueId = "movie4" });
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

        public void GallowEvents(Server server)
        {
            server.GallowTimerUpdate += Server_GallowTimerUpdate;
            server.GallowTimerElapsed += Server_GallowTimerElapsed;
            server.GallowGameEnded += Server_GallowGameEnded;
            server.GallowWordUpdate += Server_GallowWordUpdate;
        }

        private async void Server_GallowWordUpdate(Server server)
        {
            await _hub.Clients.Group(server.RoomId).playinggallows(server.GallowWord);
        }

        private async void Server_GallowTimerUpdate(int Time, Server server)
        {
            await _hub.Clients.Group(server.RoomId).gallowtimerupdate(Time);
        }

        private async void Server_GallowTimerElapsed(int Time, Server server)
        {
            await EndGallow(server);
        }

        private async void Server_GallowGameEnded(Server server)
        {
            var players = server.members.OrderByDescending(x => x.gallowPoints);
            string playerBoard = "";
            int count = 1;
            players.ToList().ForEach(x => playerBoard += $"{(count++)}. {x.username} with {x.gallowPoints} points\r\n");
            await _hub.Clients.Group(server.RoomId).dialog(new Dialog() { Header = "Gallow Game Ended", Question = playerBoard, Answer1 = "Ok" });
        }

        private async void Member_Kicked(Member e)
        {
            await KickMember(e);
        }

        public async Task PlayGallow(Server MainServer, Member sender, ChatMessage message)
        {
            if ((sender != null && sender.ishost) || sender.guessedGallow)
                return;
            if (message.message.Trim().ToLower() == MainServer.GallowWord.ToLower())
            {
                var guessedGallow = MainServer.members.Where(x => x.guessedGallow).Count();
                var points = Helper.General.GallowGuessPoints - guessedGallow;
                sender.gallowPoints += points > 0 ? points : 0;
                sender.guessedGallow = true;
                ChatMessage correntAnswerServerMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"{message.username} answered correctly" };
                await _hub.Clients.GroupExcept(MainServer.RoomId, sender.ConnectionId).sendmessage(correntAnswerServerMsg);
                ChatMessage correntAnswerPrivateMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"{message.username} you answered correct. You've been awarded {points} points" };
                await _hub.Clients.Client(sender.ConnectionId).sendmessage(correntAnswerPrivateMsg);
                if (MainServer.members.Where(x => !x.ishost).All(x => x.guessedGallow))
                {
                    await EndGallow(MainServer);
                }
                await _hub.Clients.Group(MainServer.RoomId).gallowusers(MainServer.members.Select(x => x.ToDTO()).ToList());
            }
            else
            {
                await _hub.Clients.Group(MainServer.RoomId).sendmessage(message);
            }
        }

        public async Task EndGallow(Server MainServer)
        {
            var guessedGallow = MainServer.members.Where(x => x.guessedGallow).Count();
            int idx = MainServer.members.FindIndex(x => x.ishost);
            if (idx > -1)
            {
                var hostPoints = guessedGallow * 2;
                if (hostPoints > 0)
                {
                    hostPoints += Helper.General.GallowDrawBasePoints;
                    MainServer.members[idx].gallowPoints += hostPoints;
                    ChatMessage hostMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"{MainServer.members[idx].username} {guessedGallow} users got the word correct, good job. You've been awarded {hostPoints} points" };
                    await _hub.Clients.Client(MainServer.members[idx].ConnectionId).sendmessage(hostMsg);
                }

                MainServer.members[idx].ishost = false;
                await _hub.Clients.Client(MainServer.members[idx].ConnectionId).hostupdate(false);

                idx = (idx + 1) == MainServer.members.Count ? 0 : idx + 1;
                MainServer.members[idx].ishost = true;
                await _hub.Clients.Client(MainServer.members[idx].ConnectionId).hostupdate(true);
            }
            MainServer.members.ForEach(x => { x.guessedGallow = false; x.drawings = new List<Drawing>(); });
            ChatMessage gallowEndedMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"Round has ended! The correct word was {MainServer.GallowWord}" };
            await _hub.Clients.Group(MainServer.RoomId).sendmessage(gallowEndedMsg);
            await _hub.Clients.Group(MainServer.RoomId).whiteboardclear(true);
            MainServer.UpdateGallowWord(false);
            await _hub.Clients.Group(MainServer.RoomId).userupdate(MainServer.members.Select(x => x.ToDTO()).ToList());
            if (idx == -1)
                await _hub.Clients.Group(MainServer.RoomId).gallowusers(MainServer.members.Select(x => x.ToDTO()).ToList());
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
                            room.server.members[0].drawings.AddRange(e.drawings);
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
