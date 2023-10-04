﻿using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models.Bots;

namespace SyncStreamAPI.ServerData.Helper
{
    public class RoomManager
    {
        static IServiceProvider ServiceProvider { get; set; }
        private static BlockingCollection<Room> Rooms { get; set; } = new BlockingCollection<Room>();
        private static readonly object LockObj = new object();
        public RoomManager(IServiceProvider serviceProvider, BlockingCollection<Room> rooms)
        {
            ServiceProvider = serviceProvider;
            Rooms = rooms;
        }
        public static Room? GetRoom(string UniqueId)
        {
            return GetRooms().FirstOrDefault(x => x.uniqueId == UniqueId);
        }
        public static void AddRoom(Room room)
        {
            Rooms.Add(room);
        }

        public static bool RemoveRoom(string uniqueId)
        {
            lock (LockObj)
            {
                var tempStorage = new List<Room>();
                Room room;

                // Dequeue rooms until we find the one to remove or the collection is empty
                while (Rooms.TryTake(out room))
                {
                    if (room.uniqueId == uniqueId)
                    {
                        // Re-enqueue the other rooms
                        foreach (var r in tempStorage)
                        {
                            Rooms.Add(r);
                        }
                        return true; // Room found and removed
                    }
                    tempStorage.Add(room);
                }

                // If we reach here, the room wasn't found. Re-enqueue all rooms.
                foreach (var r in tempStorage)
                {
                    Rooms.Add(r);
                }
                return false; // Room not found
            }
        }

        public static IEnumerable<Room> GetRooms()
        {
            return Rooms.ToList();
        }

        public void AddToMemberCheck(Member member)
        {
            member.Kicked += Member_Kicked;
        }

        private async void Member_Kicked(Member e)
        {
            await KickMember(e);
        }

        private async Task KickMember(Member e)
        {
            if (e != null)
            {
                try
                {
                    var room = GetRoom(e.RoomId);
                    if (room != null)
                    {                        
                        e.Kicked -= Member_Kicked;
                        if (!room.server.members.Contains(e))
                        {
                            return;
                        }

                        room.server.members.Remove(e);
                        using (var scope = ServiceProvider.CreateScope())
                        {
                            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                            if (room.server.members.Count > 0)
                            {
                                var game = room.GallowGame;
                                if (e.ishost)
                                {
                                    room.server.members[0].ishost = true;
                                    await hub.Clients.Client(room.server.members[0].ConnectionId).hostupdate(true);
                                }
                            }
                            await hub.Clients.Group(room.uniqueId).userupdate(room.server.members?.Select(x => x.ToDTO()).ToList());
                            await hub.Clients.All.getrooms(GetRooms());
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
            using var scope = ServiceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            await hub.Groups.AddToGroupAsync(connectionId, id.ToString());
        }

        public static async Task<string> AddVideo(DreckVideo key, string UniqueId)
        {
            var room = GetRoom(UniqueId);
            if (room == null)
            {
                return "Room not found";
            }
            using var scope = ServiceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var vidEnded = (room.server.currentVideo.url.Length == 0 || room.server.currentVideo.ended == true);
            if (room.server.playlist?.Select(x => x.url).Contains(key.url) == false)
            {
                room.server.currentVideo = key;
                var playerType = await SendPlayerType(room, vidEnded);
                switch (playerType)
                {
                    case PlayerType.Live:
                    {
                        if (key.title.Length == 0)
                        {
                            key.title = key.url.Split('=').Last().FirstCharToUpper();
                        }

                        break;
                    }
                    case PlayerType.External:
                    {
                        if (key.title.Length == 0)
                        {
                            key.title = "External Source";
                        }

                        break;
                    }
                    case PlayerType.Vimeo:
                    {
                        var vimeo = Vimeo.FromUrl(key.url);
                        if (vimeo != null)
                        {
                            key.title = vimeo?.Title + (vimeo?.UserName == null ? "" : " - " + vimeo.UserName);
                        }

                        break;
                    }
                    case PlayerType.YouTube when key.url.Split('?')[0].ToLower().EndsWith("playlist"):
                        await AddPlaylist(key.url, UniqueId);
                        return "";
                    case PlayerType.YouTube:
                    {
                        if (key.url.Contains("shorts"))
                        {
                            key.url = $"https://www.youtube.com/watch?v={key.url.Split('/').Last()}";
                        }

                        if (string.IsNullOrEmpty(key.title))
                        {
                            key.title = await General.ResolveUrl(key.url, config);
                        }

                        break;
                    }
                    case PlayerType.Twitch:
                    {
                        const string videoRegExString = @"\/videos\/(\d+)";
                        var videoRegEx = new Regex(videoRegExString);
                        var videoMatches = videoRegEx.Matches(key.url);
                        if (videoMatches.Count > 0)
                        {
                            key.url = videoMatches[0]?.Groups[1]?.Value;
                            key.title = $"Twitch Video - {key.url}";
                        }
                        else
                        {
                            const string titleRegExString = @"twitch.tv\/(\w+)\/?";
                            var titleRegEx = new Regex(titleRegExString);
                            var titleMatches = titleRegEx.Matches(key.url);
                            if (titleMatches.Count > 0)
                            {
                                key.title = titleMatches[0]?.Groups[1]?.Value;
                            }
                        }

                        break;
                    }
                    case PlayerType.Nothing:
                        return "The video you tried to add is not supported";
                }

                room.server.playlist.Add(key);
            }

            if (vidEnded)
            {
                room.server.currentVideo = key;
                await hub.Clients.Group(UniqueId).videoupdate(key);
            }

            await hub.Clients.Group(UniqueId).playlistupdate(room.server.playlist);
            await hub.Clients.All.getrooms(MainManager.GetRooms());
            return "";
        }
        
        

        public static async Task<PlayerType> SendPlayerType(Room room, bool sendToUsers = true,
            PlayerType type = PlayerType.Nothing)
        {
            using var scope = ServiceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            var uniqueId = room.uniqueId;
            var key = room.server.currentVideo;
            var result = PlayerType.Nothing;
            if (room.CurrentStreamer != null)
            {
                if (sendToUsers)
                {
                    await hub.Clients.Group(uniqueId).playertype(PlayerType.WebRtc);
                }

                return PlayerType.WebRtc;
            }

            if (type != PlayerType.Nothing)
            {
                if (sendToUsers)
                {
                    await hub.Clients.Group(uniqueId).playertype(type);
                }

                return type;
            }

            if (string.IsNullOrEmpty(key.url) || key.ended)
            {
                if (sendToUsers)
                {
                    await hub.Clients.Group(uniqueId).playertype(PlayerType.Nothing);
                }

                return result;
            }

            var validUri = Uri.TryCreate(key.url, UriKind.Absolute, out var uriResult) &&
                           (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
            if (!validUri)
            {
                if (sendToUsers)
                {
                    await hub.Clients.Group(uniqueId).playertype(PlayerType.Nothing);
                }

                return result;
            }

            var ytRegEx = @"^(https?\:\/\/)?((www\.)?youtube\.com|youtu\.be)\/.+$";
            var twitchRegEx = @"^(https?\:\/\/)?((www\.)?twitch\.tv)\/.+$";
            var vimeoRegEx = @"^(https?\:\/\/)?((www\.)?vimeo\.com)\/.+$";
            var ytRegex = new Regex(ytRegEx);
            var twitchRegex = new Regex(twitchRegEx);
            var vimeoRegex = new Regex(vimeoRegEx);
            if (key.url.ToLower().Contains("//live.drecktu.be/") || key.url.StartsWith("rtmp") ||
                key.url.Contains("//drecktu.be:8088/live"))
            {
                if (sendToUsers)
                {
                    await hub.Clients.Group(General.BottedInGroupName).sendBotChannelUpdate(new BotLiveChannelInfo()
                        { ChannelId = key.url.Split('=').Last(), RoomName = uniqueId });
                }

                result = PlayerType.Live;
            }
            else if (ytRegex.IsMatch(key.url))
            {
                result = PlayerType.YouTube;
            }
            else if (twitchRegex.IsMatch(key.url))
            {
                if (key.url.Contains("/clip/"))
                {
                    return PlayerType.Nothing;
                }

                result = PlayerType.Twitch;
            }
            else if (vimeoRegex.IsMatch(key.url))
            {
                result = PlayerType.Vimeo;
            }
            else
            {
                result = PlayerType.External;
            }

            if (sendToUsers)
            {
                await hub.Clients.Group(uniqueId).playertype(result);
            }

            return result;
        }
        
        

        public static async Task AddPlaylist(string url, string UniqueId)
        {
            try
            {
                var ytdl = General.GetYoutubeDL();
                var playlistInfo = await ytdl.RunVideoDataFetch(url);
                if (playlistInfo != null)
                {
                    var vids = playlistInfo.Data.Entries.Select(x => new DreckVideo(x.Title, x.Url, false,
                        TimeSpan.FromSeconds((double)(x.Duration ?? 0d)), "Playlist")).ToList();
                    vids.ForEach(async x => { await AddVideo(x, UniqueId); });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
