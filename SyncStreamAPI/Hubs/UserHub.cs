using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Chess;
using SyncStreamAPI.ServerData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        [ErrorHandling]
        public async Task AddUser(string username, string UniqueId, string password)
        {
            var ip = Context.ConnectionId;
            var room = GetRoom(UniqueId);
            if (room == null)
            {
                await Clients.Caller.adduserupdate((int)UserUpdate.RoomNotExist);
                return;
            }

            if (!string.IsNullOrEmpty(room.password) && room.password != password)
            {
                if (password == null || password.Length > 0)
                {
                    await Clients.Caller.adduserupdate((int)UserUpdate.WrongPassword);
                }

                return;
            }

            var mainServer = room.server;
            mainServer.members ??= new List<Member>();

            var newMember = new Member()
            {
                username = username, ishost = mainServer.members.Count == 0 ? true : false, ConnectionId = ip,
                RoomId = UniqueId
            };
            if (mainServer?.bannedMembers.Any(x => x.ConnectionId == newMember.ConnectionId) == true)
            {
                await Clients.Caller.adduserupdate((int)UserUpdate.Banned);
                return;
            }

            mainServer.chatmessages ??= new List<ChatMessage>();

            await Groups.AddToGroupAsync(Context.ConnectionId, UniqueId);
            mainServer.members.Add(newMember);

            await Clients.Group(UniqueId).userupdate(mainServer.members?.Select(x => x?.ToDTO()).ToList());
            switch (room.GameMode)
            {
                case GameMode.Chess:
                {
                    var game = ChessLogic.GetChessGame(room.uniqueId);
                    if (game != null)
                    {
                        await Clients.Caller.playchess(game);
                    }

                    break;
                }
                case GameMode.Gallows:
                {
                    var game = room.GallowGame;
                    if (game.members.FindIndex(x => x.ConnectionId == newMember.ConnectionId) == -1)
                    {
                        game.AddMember(newMember);
                        await Clients.Caller.playinggallows(game.GallowWord);
                        await Clients.Caller.gallowusers(game.members);
                    }

                    break;
                }
                case GameMode.Blackjack:
                {
                    var game = room.BlackjackGame;
                    if (game.members.FindIndex(x => x.ConnectionId == newMember.ConnectionId) == -1)
                    {
                        game.AddMember(newMember, _blackjackManager);
                        await Clients.Caller.playblackjack(true);
                        //give time to build component
                        await Task.Delay(250);
                        await _blackjackManager.SendAllUsers(game);
                    }

                    break;
                }
                case GameMode.NotPlaying:
                    break;
            }

            await Clients.Caller.hostupdate(newMember.ishost);
            var type = await SendPlayerType(room);
            if (type != PlayerType.Nothing)
            {
                await Clients.Caller.videoupdate(mainServer.currentVideo);
            }

            await Clients.Caller.isplayingupdate(mainServer.isplaying);
            await Clients.Caller.timeupdate(mainServer.currenttime);
            await Clients.All.getrooms(MainManager.GetRooms());
            await Clients.Caller.adduserupdate((int)UserUpdate.Success);
            if (room.CurrentStreamer != null)
            {
                await Clients.Client(room.CurrentStreamer).joinWebRtcStream(Context.ConnectionId);
            }
            if (mainServer.playlist.Count > 0)
            {
                await Clients.Caller.playlistupdate(mainServer.playlist);
            }
        }

        [ErrorHandling]
        public async Task UpdateUser(string username, string UniqueId)
        {
            var room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            var mainServer = room.server;
            var idx = mainServer.members.FindIndex(x => x != null && x.ConnectionId == Context.ConnectionId);
            if (idx != -1)
            {
                var ip = Context.ConnectionId;
                mainServer.members[idx].ConnectionId = ip;
                if (mainServer?.bannedMembers.Any(x => x.ConnectionId == mainServer.members[idx].ConnectionId) == true)
                {
                    mainServer.members.RemoveAt(idx);
                    await Clients.Caller.adduserupdate((int)UserUpdate.Banned);
                    await Clients.Group(UniqueId).userupdate(mainServer.members?.Select(x => x.ToDTO()).ToList());
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, UniqueId);
                    return;
                }

                mainServer.members[idx].uptime = DateTime.UtcNow.ToString("MM.dd.yyyy HH:mm:ss");
                if (mainServer.members.Count == 1)
                {
                    if (!mainServer.members[idx].ishost)
                    {
                        mainServer.members[idx].ishost = true;
                        await Clients.Client(mainServer.members[idx].ConnectionId).hostupdate(true);
                        await Clients.Group(UniqueId).userupdate(mainServer.members?.Select(x => x.ToDTO()).ToList());
                    }
                }
            }
            else
            {
                await AddUser(username, UniqueId, "");
                return;
            }
        }

        [ErrorHandling]
        public async Task ChangeHost(string usernameMember, string UniqueId)
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
                    Header = "Error", Question = "You don't have permissions to change the host in this room",
                    Answer1 = "Ok"
                });
                return;
            }

            var mainServer = room.server;
            var idxHost = mainServer.members.FindIndex(x => x.ishost == true);
            var idxMember = mainServer.members.FindIndex(x =>
                !x.ishost && (x.username == usernameMember || x.ConnectionId == usernameMember));
            if (idxMember != -1)
            {
                var game = room.GallowGame;
                if (game != null && game.PlayingGallows)
                {
                    game.UpdateGallowWord(true);
                }

                if (idxHost != -1)
                {
                    mainServer.members[idxHost].uptime = DateTime.UtcNow.ToString("MM.dd.yyyy HH:mm:ss");
                    mainServer.members[idxHost].ishost = false;
                    await Clients.Client(mainServer.members[idxHost].ConnectionId).hostupdate(false);
                }

                mainServer.members[idxMember].uptime = DateTime.UtcNow.ToString("MM.dd.yyyy HH:mm:ss");
                mainServer.members[idxMember].ishost = true;
                await Clients.Client(mainServer.members[idxMember].ConnectionId).hostupdate(true);
                await Clients.Group(UniqueId).userupdate(mainServer.members?.Select(x => x.ToDTO()).ToList());
            }
        }

        [ErrorHandling]
        public async Task RemoveUser(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            var mainServer = room.server;
            var member = mainServer.members.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
            if (member == null)
            {
                return;
            }

            bool isHost = member.ishost;
            mainServer.members.Remove(member);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, UniqueId);
            if (mainServer.members.Count > 0)
            {
                var gallowGame = room.GallowGame;
                if (gallowGame != null && gallowGame.PlayingGallows)
                {
                    if (mainServer.members.Count < 2)
                    {
                        await PlayGallowsSettings(UniqueId, gallowGame.GameLanguage, gallowGame.GameLength);
                    }
                }

                if (isHost)
                {
                    if (gallowGame != null && gallowGame.PlayingGallows)
                    {
                        gallowGame.UpdateGallowWord(true);
                    }

                    if (mainServer.members.Count > 0 && mainServer.members[0] != null)
                    {
                        mainServer.members[0].ishost = true;
                    }

                    await Clients.Client(mainServer.members[0]?.ConnectionId).hostupdate(true);
                }
            }

            if (room.GameMode == Enums.Games.GameMode.Chess)
            {
                var game = ChessLogic.GetChessGame(room.uniqueId);
                if (game != null && (game.LightPlayer.ConnectionId == Context.ConnectionId ||
                                     game.DarkPlayer.ConnectionId == Context.ConnectionId))
                {
                    await EndChess(room.uniqueId);
                }
            }

            var blackjack = room.BlackjackGame;
            if (blackjack != null)
            {
                var idx = blackjack.members.FindIndex(x => x.ConnectionId == member.ConnectionId);
                if (idx != -1)
                {
                    var bjMember = blackjack.members[idx];
                    blackjack.members.RemoveAt(idx);
                    if (bjMember.waitingForBet)
                    {
                        bjMember.waitingForBet = false;
                        _blackjackManager.AskForBet(blackjack, idx + 1);
                    }
                    else if (bjMember.waitingForPull)
                    {
                        bjMember.waitingForPull = false;
                        _blackjackManager.AskForPull(blackjack, idx + 1);
                        await _blackjackManager.SendAllUsers(blackjack);
                    }
                    else
                    {
                        await _blackjackManager.SendAllUsers(blackjack);
                    }
                }

                if (blackjack.members.Count < 1)
                {
                    await _blackjackManager.PlayNewRound(UniqueId);
                }
            }

            await Clients.Group(UniqueId).userupdate(mainServer.members?.Select(x => x?.ToDTO()).ToList());
            await Clients.All.getrooms(MainManager.GetRooms());
        }

        [ErrorHandling]
        public async Task BanUser(string username, string UniqueId)
        {
            var room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            var mainServer = room.server;
            var member = mainServer.members.FirstOrDefault(x => x.username == username);
            if (member != null)
            {
                mainServer.members.Remove(member);
                mainServer.bannedMembers.Add(member);
                if (member.ishost && mainServer.members.Count > 0)
                {
                    mainServer.members[0].ishost = true;
                    await Clients.Client(mainServer.members[0].ConnectionId).hostupdate(true);
                    await Clients.Group(UniqueId).userupdate(mainServer.members?.Select(x => x.ToDTO()).ToList());
                }
            }

            await Clients.User(member.ConnectionId).adduserupdate((int)UserUpdate.Banned);
            await Clients.Group(UniqueId).userupdate(mainServer.members?.Select(x => x.ToDTO()).ToList());
            await Clients.All.getrooms(MainManager.GetRooms());
        }
    }
}