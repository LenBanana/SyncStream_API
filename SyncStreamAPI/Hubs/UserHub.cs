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

        public async Task AddUser(string username, string UniqueId, string password)
        {
            var ip = Context.ConnectionId;
            Room room = GetRoom(UniqueId);
            if (room == null)
            {
                await Clients.Caller.adduserupdate((int)UserUpdate.RoomNotExist);
                return;
            }
            if (room.password != null && room.password.Length > 0 && room.password != password)
            {
                if (password == null || password.Length > 0)
                {
                    await Clients.Caller.adduserupdate((int)UserUpdate.WrongPassword);
                }

                return;
            }
            Server MainServer = room.server;
            if (MainServer.members == null)
            {
                MainServer.members = new List<Member>();
            }
            Member newMember = new Member() { username = username, ishost = MainServer.members.Count == 0 ? true : false, ConnectionId = ip, RoomId = UniqueId };
            if (MainServer?.bannedMembers.Any(x => x.ConnectionId == newMember.ConnectionId) == true)
            {
                await Clients.Caller.adduserupdate((int)UserUpdate.Banned);
                return;
            }
            if (MainServer.chatmessages == null)
            {
                MainServer.chatmessages = new List<ChatMessage>();
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, UniqueId);
            MainServer.members.Add(newMember);

            await Clients.Group(UniqueId).userupdate(MainServer.members?.Select(x => x?.ToDTO()).ToList());
            if (room.GameMode == GameMode.Chess)
            {
                var game = ChessLogic.GetChessGame(room.uniqueId);
                if (game != null)
                {
                    await Clients.Caller.playchess(game);
                }
            }
            if (room.GameMode == GameMode.Gallows)
            {
                var game = room.GallowGame;
                if (game.members.FindIndex(x => x.ConnectionId == newMember.ConnectionId) == -1)
                {
                    game.AddMember(newMember);
                    await Clients.Caller.playinggallows(game.GallowWord);
                    await Clients.Caller.gallowusers(game.members);
                }
            }
            if (room.GameMode == GameMode.Blackjack)
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
            }
            await Clients.Caller.hostupdate(newMember.ishost);
            var type = await SendPlayerType(UniqueId, MainServer.currentVideo);
            if (type != PlayerType.Nothing)
            {
                await Clients.Caller.videoupdate(MainServer.currentVideo);
            }

            await Clients.Caller.isplayingupdate(MainServer.isplaying);
            await Clients.Caller.timeupdate(MainServer.currenttime);
            await Clients.All.getrooms(DataManager.GetRooms());
            await Clients.Caller.adduserupdate((int)UserUpdate.Success);
            if (MainServer.playlist.Count > 0)
            {
                await Clients.Caller.playlistupdate(MainServer.playlist);
            }
        }

        public async Task UpdateUser(string username, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            Server MainServer = room.server;
            int idx = MainServer.members.FindIndex(x => x != null && x.ConnectionId == Context.ConnectionId);
            if (idx != -1)
            {
                var ip = Context.ConnectionId;
                MainServer.members[idx].ConnectionId = ip;
                if (MainServer?.bannedMembers.Any(x => x.ConnectionId == MainServer.members[idx].ConnectionId) == true)
                {
                    MainServer.members.RemoveAt(idx);
                    await Clients.Caller.adduserupdate((int)UserUpdate.Banned);
                    await Clients.Group(UniqueId).userupdate(MainServer.members?.Select(x => x.ToDTO()).ToList());
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
                        await Clients.Group(UniqueId).userupdate(MainServer.members?.Select(x => x.ToDTO()).ToList());
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
            {
                return;
            }

            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "You don't have permissions to change the host in this room", Answer1 = "Ok" });
                return;
            }
            Server MainServer = room.server;
            int idxHost = MainServer.members.FindIndex(x => x.ishost == true);
            int idxMember = MainServer.members.FindIndex(x => !x.ishost && (x.username == usernameMember || x.ConnectionId == usernameMember));
            if (idxMember != -1)
            {
                var game = room.GallowGame;
                if (game != null && game.PlayingGallows)
                {
                    game.UpdateGallowWord(true);
                }

                if (idxHost != -1)
                {
                    MainServer.members[idxHost].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                    MainServer.members[idxHost].ishost = false;
                    await Clients.Client(MainServer.members[idxHost].ConnectionId).hostupdate(false);
                }
                MainServer.members[idxMember].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                MainServer.members[idxMember].ishost = true;
                await Clients.Client(MainServer.members[idxMember].ConnectionId).hostupdate(true);
                await Clients.Group(UniqueId).userupdate(MainServer.members?.Select(x => x.ToDTO()).ToList());
            }
        }

        public async Task RemoveUser(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            Server MainServer = room.server;
            Member member = MainServer.members.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
            if (member == null)
            {
                return;
            }

            bool isHost = member.ishost;
            MainServer.members.Remove(member);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, UniqueId);
            if (MainServer.members.Count > 0)
            {
                var gallowGame = room.GallowGame;
                if (gallowGame != null && gallowGame.PlayingGallows)
                {
                    if (MainServer.members.Count < 2)
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

                    if (MainServer.members.Count > 0 && MainServer.members[0] != null)
                    {
                        MainServer.members[0].ishost = true;
                    }

                    await Clients.Client(MainServer.members[0]?.ConnectionId).hostupdate(true);
                }
            }
            if (room.GameMode == Enums.Games.GameMode.Chess)
            {
                var game = ChessLogic.GetChessGame(room.uniqueId);
                if (game != null && (game.LightPlayer.ConnectionId == Context.ConnectionId || game.DarkPlayer.ConnectionId == Context.ConnectionId))
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
            await Clients.Group(UniqueId).userupdate(MainServer.members?.Select(x => x?.ToDTO()).ToList());
            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task BanUser(string username, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
            {
                return;
            }

            Server MainServer = room.server;
            var member = MainServer.members.FirstOrDefault(x => x.username == username);
            if (member != null)
            {
                MainServer.members.Remove(member);
                MainServer.bannedMembers.Add(member);
                if (member.ishost && MainServer.members.Count > 0)
                {
                    MainServer.members[0].ishost = true;
                    await Clients.Client(MainServer.members[0].ConnectionId).hostupdate(true);
                    await Clients.Group(UniqueId).userupdate(MainServer.members?.Select(x => x.ToDTO()).ToList());
                }
            }
            await Clients.User(member.ConnectionId).adduserupdate((int)UserUpdate.Banned);
            await Clients.Group(UniqueId).userupdate(MainServer.members?.Select(x => x.ToDTO()).ToList());
            await Clients.All.getrooms(DataManager.GetRooms());
        }
    }
}
