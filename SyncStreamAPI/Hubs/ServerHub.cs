using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Games.Gallows;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.Bots;
using SyncStreamAPI.Models.GameModels.Chess;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SyncStreamAPI.Enums.Games;

namespace SyncStreamAPI.Hubs
{
    [EnableCors("CORSPolicy")]
    public partial class ServerHub : Hub<IServerHub>
    {
        IConfiguration Configuration { get; }

        readonly MainManager _manager;
        readonly PostgresContext _postgres;
        readonly GallowGameManager _gallowGameManager;
        readonly BlackjackManager _blackjackManager;

        public ServerHub(IConfiguration configuration, MainManager manager, PostgresContext postgres,
            GallowGameManager gallowGameManager, BlackjackManager blackjackManager)
        {
            Configuration = configuration;
            _manager = manager;
            _postgres = postgres;
            _gallowGameManager = gallowGameManager;
            _blackjackManager = blackjackManager;
        }

#nullable enable
        public override async Task OnDisconnectedAsync(Exception? ex)
        {
            var rooms = MainManager.GetRooms();
            var room = rooms.FirstOrDefault(x =>
                x.server.members.FirstOrDefault(y => y?.ConnectionId == Context.ConnectionId) != null);
            if (room != null)
            {
                var e = room.server.members.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
                e?.InvokeKick();
                if (room.CurrentStreamer == Context.ConnectionId)
                {
                    room.CurrentStreamer = null;
                    await Clients.Group(room.uniqueId).stopWebRtcStream(Context.ConnectionId);
                }

                var gameMode = room.GameMode;
                switch (gameMode)
                {
                    case Enums.Games.GameMode.Chess:
                    {
                        var game = ChessLogic.GetChessGame(room.uniqueId);
                        if (game != null && (game.LightPlayer.ConnectionId == e?.ConnectionId ||
                                             game.DarkPlayer.ConnectionId == e?.ConnectionId))
                        {
                            await EndChess(room.uniqueId);
                        }

                        break;
                    }
                    case Enums.Games.GameMode.Gallows:
                    {
                        var game = room.GallowGame;
                        var gameMemberIdx = game.members.FindIndex(x => x.ConnectionId == e?.ConnectionId);
                        if (gameMemberIdx > -1)
                        {
                            game.members.RemoveAt(gameMemberIdx);
                        }

                        break;
                    }
                    case Enums.Games.GameMode.Blackjack:
                    {
                        var blackjack = room.BlackjackGame;
                        var gameMemberIdx = blackjack.members.FindIndex(x => x.ConnectionId == e?.ConnectionId);
                        if (gameMemberIdx > -1)
                        {
                            var bjMember = blackjack.members[gameMemberIdx];
                            blackjack.members.RemoveAt(gameMemberIdx);
                            if (bjMember.waitingForBet)
                            {
                                bjMember.waitingForBet = false;
                                _blackjackManager.AskForBet(blackjack, gameMemberIdx + 1);
                            }
                            else if (bjMember.waitingForPull)
                            {
                                bjMember.waitingForPull = false;
                                _blackjackManager.AskForPull(blackjack, gameMemberIdx + 1);
                                await _blackjackManager.SendAllUsers(blackjack);
                            }
                            else
                            {
                                await _blackjackManager.SendAllUsers(blackjack);
                            }
                        }

                        if (blackjack.members.Count < 1)
                        {
                            await _blackjackManager.PlayNewRound(blackjack.RoomId);
                        }
                        else
                        {
                            await _blackjackManager.SendAllUsers(blackjack);
                        }

                        break;
                    }
                    case GameMode.NotPlaying:
                        break;
                }
            }

            await base.OnDisconnectedAsync(ex);
        }

        public bool CheckPrivileges(Room room, int minPriv = 1)
        {
            if (room.isPrivileged)
            {
                var member = room.server.members.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
                if (member != null)
                {
                    if (member.ishost)
                    {
                        return true;
                    }

                    var user = _postgres.Users.FirstOrDefault(x => x.username == member.username);
                    if (user == null || user.userprivileges < (UserPrivileges)minPriv)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        public async Task SendServer(Server server, string UniqueId)
        {
            await Clients.Group(UniqueId).sendserver(server);
        }

        private Room? GetRoom(string UniqueId)
        {
            BlockingCollection<Room> Rooms = MainManager.GetRooms();
            Room? room = Rooms.FirstOrDefault(x => x.uniqueId == UniqueId);
            if (room == null)
            {
                return null;
            }

            return room;
        }

        public async Task GetRooms()
        {
            await Clients.Caller.getrooms(MainManager.GetRooms());
        }

        public async Task AddVideo(DreckVideo key, string UniqueId)
        {
            var room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                {
                    Header = "Error", Question = "You don't have permissions to add a video to this room",
                    Answer1 = "Ok"
                });
                return;
            }

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
                        return;
                    case PlayerType.YouTube:
                    {
                        if (key.url.Contains("shorts"))
                        {
                            key.url = $"https://www.youtube.com/watch?v={key.url.Split('/').Last()}";
                        }

                        if (string.IsNullOrEmpty(key.title))
                        {
                            key.title = await General.ResolveURL(key.url, Configuration);
                        }

                        break;
                    }
                    case PlayerType.Twitch:
                    {
                        var videoRegExString = @"\/videos\/(\d+)";
                        var videoRegEx = new Regex(videoRegExString);
                        var videoMatches = videoRegEx.Matches(key.url);
                        if (videoMatches != null && videoMatches.Count > 0)
                        {
                            key.url = videoMatches[0]?.Groups[1]?.Value;
                            key.title = $"Twitch Video - {key.url}";
                        }
                        else
                        {
                            var titleRegExString = @"twitch.tv\/(\w+)\/?";
                            var titleRegEx = new Regex(titleRegExString);
                            var titleMatches = titleRegEx.Matches(key.url);
                            if (titleMatches != null && titleMatches.Count > 0)
                            {
                                key.title = titleMatches[0]?.Groups[1]?.Value;
                            }
                        }

                        break;
                    }
                    case PlayerType.Nothing:
                        await Clients.Caller.dialog(new Dialog(AlertType.Warning)
                            { Header = "Denied", Question = "Given input is not allowed", Answer1 = "Ok" });
                        return;
                }

                room.server.playlist.Add(key);
            }

            if (vidEnded)
            {
                room.server.currentVideo = key;
                await Clients.Group(UniqueId).videoupdate(key);
            }

            await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
            await Clients.All.getrooms(MainManager.GetRooms());
        }

        public async Task<PlayerType> SendPlayerType(Room room, bool sendToUsers = true,
            PlayerType type = PlayerType.Nothing)
        {
            var uniqueId = room.uniqueId;
            var key = room.server.currentVideo;
            var result = PlayerType.Nothing;
            if (room.CurrentStreamer != null)
            {
                if (sendToUsers)
                {
                    await Clients.Group(uniqueId).playertype(PlayerType.WebRtc);
                }

                return PlayerType.WebRtc;
            }
            if (type != PlayerType.Nothing)
            {
                if (sendToUsers)
                {
                    await Clients.Group(uniqueId).playertype(type);
                }

                return type;
            }

            if (string.IsNullOrEmpty(key.url) || key.ended)
            {
                if (sendToUsers)
                {
                    await Clients.Group(uniqueId).playertype(PlayerType.Nothing);
                }

                return result;
            }

            var validUri = Uri.TryCreate(key.url, UriKind.Absolute, out var uriResult) &&
                           (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
            if (!validUri)
            {
                if (sendToUsers)
                {
                    await Clients.Group(uniqueId).playertype(PlayerType.Nothing);
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
                    await Clients.Group(General.BottedInGroupName).sendBotChannelUpdate(new BotLiveChannelInfo()
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
                await Clients.Group(uniqueId).playertype(result);
            }

            return result;
        }

        public async Task AddPlaylist(string url, string UniqueId)
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
                await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                {
                    Header = "Error", Question = "There has been an error trying to add the playlist", Answer1 = "Ok"
                });
            }
        }

        public async Task RemoveVideo(string key, string UniqueId)
        {
            Room? room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                {
                    Header = "Error", Question = "You don't have permissions to remove videos from this room",
                    Answer1 = "Ok"
                });
                return;
            }

            int idx = room.server.playlist.FindIndex(x => x.url == key);
            if (idx != -1)
            {
                room.server.playlist.RemoveAt(idx);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(MainManager.GetRooms());
            }
        }

        public async Task NextVideo(string UniqueId)
        {
            Room? room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                {
                    Header = "Error", Question = "You don't have permissions to skip videos in this room",
                    Answer1 = "Ok"
                });
                return;
            }

            Server MainServer = room.server;
            if (room.server.playlist.Count > 1)
            {
                MainServer.currentVideo = room.server.playlist[1];
                room.server.playlist.RemoveAt(0);
                await SendPlayerType(room);
                await Clients.Group(UniqueId).videoupdate(MainServer.currentVideo);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(MainManager.GetRooms());
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
                await Clients.Group(UniqueId).playertype(PlayerType.Nothing);
                await Clients.All.getrooms(MainManager.GetRooms());
            }
        }

        public async Task PlayVideo(string key, string UniqueId)
        {
            Room? room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                {
                    Header = "Error", Question = "You don't have permissions to skip videos in this room",
                    Answer1 = "Ok"
                });
                return;
            }

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
                await Clients.All.getrooms(MainManager.GetRooms());
                return;
            }
        }

        public async Task MoveVideo(int fromIndex, int toIndex, string UniqueId)
        {
            Room? room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                {
                    Header = "Error", Question = "You don't have permissions to move videos in this room",
                    Answer1 = "Ok"
                });
                return;
            }

            DreckVideo vid = room.server.playlist[fromIndex];
            room.server.playlist.RemoveAt(fromIndex);
            room.server.playlist.Insert(toIndex, vid);
            await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
        }

        public async Task SetTime(double time, string UniqueId)
        {
            Room? room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            if (!CheckPrivileges(room))
            {
                return;
            }

            //if (room.server.currentVideo.url.Contains("twitch.tv"))
            //    await Clients.Group(UniqueId).twitchTimeUpdate(time);
            //else
            await Clients.Group(UniqueId).timeupdate(time);
            room.server.currenttime = time;
        }

        public async Task PlayPause(bool isplaying, string UniqueId)
        {
            Room? room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            if (!CheckPrivileges(room))
            {
                return;
            }

            room.server.isplaying = isplaying;
            //if (room.server.currentVideo.url.Contains("twitch.tv"))
            //    await Clients.Group(UniqueId).twitchPlaying(isplaying);
            //else
            await Clients.Group(UniqueId).isplayingupdate(isplaying);
            await Clients.All.getrooms(MainManager.GetRooms());
        }

        public async Task AddRoom(Room room, string token)
        {
            var Rooms = MainManager.GetRooms();
            int RoomCount = 0;
            while (Rooms?.Any(x => x.uniqueId == room.uniqueId) == true)
            {
                room.uniqueId = room.uniqueId + RoomCount++;
            }

            Rooms?.Add(room);
            if (!room.deletable)
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x =>
                    x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
                var tokenObj = dbUser?.RememberTokens.SingleOrDefault(x => x.Token == token);
                if (tokenObj == null || dbUser == null || dbUser.userprivileges < UserPrivileges.Administrator)
                {
                    if (dbUser != null)
                    {
                        var errorMessage = "You do not have permissions to make a room permanent";
                        await Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(Enums.AlertType.Danger)
                            { Question = errorMessage, Answer1 = "Ok" });
                        return;
                    }

                    return;
                }

                DbRoom dbRoom = new DbRoom(room);
                var roomEntity = await _postgres.Rooms.AddAsync(dbRoom);
                await _postgres.SaveChangesAsync();
            }

            await Clients.All.getrooms(MainManager.GetRooms());
        }

        public async Task ChangeRoom(Room room)
        {
            var Rooms = MainManager.GetRooms();
            var changeRoom = Rooms.FirstOrDefault(x => x.uniqueId == room.uniqueId);
            if (changeRoom != null)
            {
                changeRoom.name = room.name;
                changeRoom.password = room.password;
                changeRoom.deletable = room.deletable;
                await Clients.All.getrooms(MainManager.GetRooms());
            }
        }

        public async Task RemoveRoom(string UniqueId, string token)
        {
            Room? room = GetRoom(UniqueId);
            if (room == null || room.isPrivileged)
            {
                return;
            }

            var dbRoom = _postgres.Rooms.FirstOrDefault(x => x.uniqueId == UniqueId);
            if (dbRoom != null)
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x =>
                    x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
                var tokenObj = dbUser?.RememberTokens.SingleOrDefault(x => x.Token == token);
                if (tokenObj == null || dbUser == null || dbUser.userprivileges < UserPrivileges.Administrator)
                {
                    if (dbUser != null)
                    {
                        var errorMessage = "You do not have permissions to delete this room";
                        await Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(Enums.AlertType.Danger)
                            { Question = errorMessage, Answer1 = "Ok" });
                        return;
                    }

                    return;
                }

                _postgres.Rooms.Remove(dbRoom);
                await _postgres.SaveChangesAsync();
            }

            MainManager.GetRooms().TryTake(out room);
            await Clients.All.getrooms(MainManager.GetRooms());
        }

        public async Task Ping(DateTime date)
        {
            await Clients.Caller.PingTest(date);
        }
    }
}