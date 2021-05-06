using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Hubs;
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
        private readonly IHubContext<ServerHub> _hub;

        public DataManager(IHubContext<ServerHub> hub)
        {
            _hub = hub;
            Random rnd = new Random();
            Rooms.Add(new Room() { name = "Dreckroom", server = new Server(), uniqueId = "dreck" });
            Rooms.Add(new Room() { name = "Randomkeller", server = new Server(), uniqueId = "random" });
            //Rooms.Add(new Room() { name = "PrivateRoom", password = "dreckpass", server = new Server(), uniqueId = id++ });
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
            if (e != null)
            {
                try
                {
                    int idx = Rooms.FindIndex(x => x.uniqueId == e.RoomId);
                    if (idx > -1)
                    {
                        Room room = Rooms[idx];
                        e.Kicked -= Member_Kicked;
                        room.server.members.Remove(e);
                        
                        if (room.server.members.Count > 0)
                        {
                            room.server.members[0].drawings.AddRange(e.drawings);
                            if (e.ishost)
                            {
                                room.server.members[0].ishost = true;
                                await _hub.Clients.Group(room.uniqueId).SendAsync("hostupdate" + room.server.members[0].username, true);
                            }
                        }
                        await _hub.Clients.Group(room.uniqueId).SendAsync("userupdate", room.server.members);
                        await _hub.Clients.All.SendAsync("getrooms", Rooms);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        //public async void CheckMembers()
        //{
        //    List<Room> rooms = new List<Room>(Rooms);
        //    foreach (Room room in rooms)
        //    {
        //        if (room.server.members == null)
        //            room.server.members = new List<Member>();
        //        List<Member> tempMembers = new List<Member>(room.server.members);
        //        foreach(Member member in tempMembers)
        //        {
        //            if (member != null && member.kick == true)
        //            {
        //                try
        //                {
        //                    room.server.members.Remove(member);
        //                    if (room.server.members.Count > 0)
        //                    {
        //                        room.server.members[0].drawings.AddRange(member.drawings);
        //                        if (member.ishost)
        //                        {
        //                            room.server.members[0].ishost = true;
        //                            await _hub.Clients.Group(room.uniqueId).SendAsync("hostupdate" + room.server.members[0].username, true);
        //                        }
        //                    }
        //                    await _hub.Clients.Group(room.uniqueId).SendAsync("userupdate", room.server.members);
        //                    await _hub.Clients.All.SendAsync("getrooms", Rooms);
        //                } catch
        //                {
        //                    await Task.Delay(2500);
        //                    CheckMembers();
        //                }
        //            }
        //        }
        //    }
        //    await Task.Delay(2500);
        //    CheckMembers();
        //}

        public async void wc_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e, string id)
        {
            float received = (e.BytesReceived / 1024f) / 1024f;
            float total = (e.TotalBytesToReceive / 1024f) / 1024f;
            string status = "Download at: " + e.ProgressPercentage + "%. Processed " + received + "mb of " + total + "mb total."; 
            await _hub.Clients.All.SendAsync("dlUpdate" + id, e);
        }

        public async void Completed(object o, AsyncCompletedEventArgs args, string id)
        {
            await _hub.Clients.All.SendAsync("dlUpdate" + id, "Download completed");
        }
    }
}
