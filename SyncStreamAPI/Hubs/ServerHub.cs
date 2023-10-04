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
using SyncStreamAPI.ServerData.Helper;

namespace SyncStreamAPI.Hubs
{
    [EnableCors("CORSPolicy")]
    public partial class ServerHub : Hub<IServerHub>
    {
        IConfiguration Configuration { get; }

        readonly MainManager _manager;

        //readonly WebRtcSfuManager _webRtcSfuManager;
        readonly PostgresContext _postgres;
        readonly GallowGameManager _gallowGameManager;
        readonly BlackjackManager _blackjackManager;

        public ServerHub(IConfiguration configuration, MainManager manager, PostgresContext postgres,
            GallowGameManager gallowGameManager, BlackjackManager blackjackManager) //WebRtcSfuManager webRtcSfuManager
        {
            Configuration = configuration;
            _manager = manager;
            //_webRtcSfuManager = webRtcSfuManager;
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
            if (!room.isPrivileged) return true;
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

            return true;
        }

        public async Task SendServer(Server server, string UniqueId)
        {
            await Clients.Group(UniqueId).sendserver(server);
        }

        private Room? GetRoom(string UniqueId)
        {
            var rooms = MainManager.GetRooms();
            var room = rooms.FirstOrDefault(x => x.uniqueId == UniqueId);
            return room ?? null;
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

            var result = await RoomManager.AddVideo(key, UniqueId);
            if (!string.IsNullOrEmpty(result))
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Warning)
                    { Header = "Denied", Question = result, Answer1 = "Ok" });
            }
        }

        public async Task RemoveVideo(string key, string UniqueId)
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
                    Header = "Error", Question = "You don't have permissions to remove videos from this room",
                    Answer1 = "Ok"
                });
                return;
            }

            var idx = room.server.playlist.FindIndex(x => x.url == key);
            if (idx != -1)
            {
                room.server.playlist.RemoveAt(idx);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(MainManager.GetRooms());
            }
        }

        public async Task NextVideo(string UniqueId)
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
                    Header = "Error", Question = "You don't have permissions to skip videos in this room",
                    Answer1 = "Ok"
                });
                return;
            }

            var mainServer = room.server;
            switch (room.server.playlist.Count)
            {
                case > 1:
                    mainServer.currentVideo = room.server.playlist[1];
                    room.server.playlist.RemoveAt(0);
                    await RoomManager.SendPlayerType(room);
                    await Clients.Group(UniqueId).videoupdate(mainServer.currentVideo);
                    await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                    await Clients.All.getrooms(MainManager.GetRooms());
                    return;
                case 1:
                    mainServer.currentVideo.ended = true;
                    room.server.playlist.RemoveAt(0);
                    room.server.isplaying = false;
                    await Clients.Group(UniqueId).isplayingupdate(room.server.isplaying);
                    await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                    await Clients.Group(UniqueId).sendserver(room.server);
                    await Clients.Group(UniqueId).playertype(PlayerType.Nothing);
                    await Clients.All.getrooms(MainManager.GetRooms());
                    break;
            }
        }

        public async Task PlayVideo(string key, string UniqueId)
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
                    Header = "Error", Question = "You don't have permissions to skip videos in this room",
                    Answer1 = "Ok"
                });
                return;
            }

            var mainServer = room.server;
            var idx = mainServer.playlist.FindIndex(x => x.url == key);
            if (idx != -1)
            {
                var tempUrl = room.server.playlist[idx];
                room.server.playlist.RemoveAt(idx);
                room.server.playlist.RemoveAt(0);
                room.server.playlist.Insert(0, tempUrl);
                mainServer.currentVideo = room.server.playlist[0];
                await Clients.Group(UniqueId).videoupdate(mainServer.currentVideo);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(MainManager.GetRooms());
                return;
            }
        }

        public async Task MoveVideo(int fromIndex, int toIndex, string UniqueId)
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
                    Header = "Error", Question = "You don't have permissions to move videos in this room",
                    Answer1 = "Ok"
                });
                return;
            }

            var vid = room.server.playlist[fromIndex];
            room.server.playlist.RemoveAt(fromIndex);
            room.server.playlist.Insert(toIndex, vid);
            await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
        }

        public async Task SetTime(double time, string UniqueId)
        {
            var room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            if (!CheckPrivileges(room))
            {
                return;
            }

            await Clients.Group(UniqueId).timeupdate(time);
            room.server.currenttime = time;
        }

        public async Task PlayPause(bool isplaying, string UniqueId)
        {
            var room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            if (!CheckPrivileges(room))
            {
                return;
            }

            room.server.isplaying = isplaying;
            await Clients.Group(UniqueId).isplayingupdate(isplaying);
            await Clients.All.getrooms(MainManager.GetRooms());
        }

        public async Task AddRoom(Room room, string token)
        {
            var rooms = MainManager.GetRooms();
            var roomCount = 0;
            while (rooms?.Any(x => x.uniqueId == room.uniqueId) == true)
            {
                room.uniqueId = room.uniqueId + roomCount++;
            }

            RoomManager.AddRoom(room);
            if (!room.deletable)
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x =>
                    x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
                var tokenObj = dbUser?.RememberTokens.SingleOrDefault(x => x.Token == token);
                if (tokenObj == null || dbUser == null || dbUser.userprivileges < UserPrivileges.Administrator)
                {
                    if (dbUser == null) return;
                    const string errorMessage = "You do not have permissions to make a room permanent";
                    await Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(Enums.AlertType.Danger)
                        { Question = errorMessage, Answer1 = "Ok" });
                    return;
                }

                var dbRoom = new DbRoom(room);
                await _postgres.Rooms.AddAsync(dbRoom);
                await _postgres.SaveChangesAsync();
            }

            await Clients.All.getrooms(MainManager.GetRooms());
        }

        public async Task ChangeRoom(Room room)
        {
            var rooms = MainManager.GetRooms();
            var changeRoom = rooms.FirstOrDefault(x => x.uniqueId == room.uniqueId);
            if (changeRoom != null)
            {
                changeRoom.name = room.name;
                changeRoom.password = room.password;
                changeRoom.deletable = room.deletable;
                await Clients.All.getrooms(MainManager.GetRooms());
            }
        }

        public async Task RemoveRoom(string uniqueId, string token)
        {
            var room = GetRoom(uniqueId);
            if (room == null || room.isPrivileged)
            {
                return;
            }

            var dbRoom = _postgres.Rooms.FirstOrDefault(x => x.uniqueId == uniqueId);
            if (dbRoom == null)
            {
                return;
            }

            var dbUser = _postgres.Users?.Include(x => x.RememberTokens)
                .FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == token) == true
                                     && x.userprivileges >= UserPrivileges.Administrator);

            if (dbUser == null)
            {
                const string errorMessage = "You do not have permissions to delete this room";
                await Clients.Caller.dialog(new Dialog(Enums.AlertType.Danger, errorMessage));
                return;
            }

            _postgres.Rooms.Remove(dbRoom);
            await _postgres.SaveChangesAsync();

            if (RoomManager.RemoveRoom(uniqueId))
            {
                await Clients.All.getrooms(MainManager.GetRooms());
            }
            else
            {
                var errorMessage = $"There was an error attempting to remove {room.name}";
                await Clients.Caller.dialog(new Dialog(Enums.AlertType.Danger, errorMessage));
            }
        }

        public async Task Ping(DateTime date)
        {
            await Clients.Caller.PingTest(date);
        }
    }
}