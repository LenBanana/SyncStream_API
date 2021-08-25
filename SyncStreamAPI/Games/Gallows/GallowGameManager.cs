using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Gallows;
using SyncStreamAPI.ServerData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;


namespace SyncStreamAPI.Games.Gallows
{
    public class GallowGameManager
    {
        private readonly IHubContext<ServerHub, IServerHub> _hub;

        public static List<GallowLogic> gallowGames { get; set; } = new List<GallowLogic>();

        public GallowGameManager(IHubContext<ServerHub, IServerHub> hub)
        {
            _hub = hub;
        }

        public bool PlayNewRound(string UniqueId)
        {
            var idx = gallowGames.FindIndex(x => x.RoomId == UniqueId);
            if (idx < 0)
            {
                Room room = DataManager.GetRoom(UniqueId);
                gallowGames.Add(new GallowLogic(this, UniqueId, room.server.members.Select(x => x.ToGallowMember()).ToList()));
                return true;
            }
            gallowGames[idx].PlayingGallows = false;
            gallowGames.RemoveAt(idx);
            return false;
        }

        public void GallowEvents(GallowLogic game)
        {
            game.GallowTimerUpdate += Server_GallowTimerUpdate;
            game.GallowTimerElapsed += Server_GallowTimerElapsed;
            game.GallowGameEnded += Server_GallowGameEnded;
            game.GallowWordUpdate += Server_GallowWordUpdate;
        }

        public static Server GetGameRoom(string UniqueId)
        {
            var room = DataManager.GetRoom(UniqueId);
            return room == null ? null : room.server;
        }

        private async void Server_GallowWordUpdate(GallowLogic game)
        {
            await _hub.Clients.Group(game.RoomId).playinggallows(game.GallowWord);
        }

        private async void Server_GallowTimerUpdate(int Time, GallowLogic game)
        {
            if (Time % 5 == 0)
                await _hub.Clients.Group(game.RoomId).gallowusers(game.members);
            await _hub.Clients.Group(game.RoomId).gallowtimerupdate(Time);
        }

        private async void Server_GallowTimerElapsed(int Time, GallowLogic game)
        {
            await EndGallow(game, Time);
        }

        private async void Server_GallowGameEnded(GallowLogic game)
        {
            var players = game.members.OrderByDescending(x => x.gallowPoints);
            string playerBoard = "";
            int count = 1;
            players.ToList().ForEach(x => playerBoard += $"{(count++)}. {x.username} with {x.gallowPoints} points\r\n");
            await _hub.Clients.Group(game.RoomId).dialog(new Dialog() { Header = "Gallow Game Ended", Question = playerBoard, Answer1 = "Ok" });
        }

        public async Task PlayGallow(GallowLogic game, Member sender, ChatMessage message, int Time)
        {
            var gallowMember = game.members.FirstOrDefault(x => x.username == sender.username);
            if ((sender != null && sender.ishost) || gallowMember.guessedGallow)
                return;
            string msg = message.message.Trim().ToLower();
            string gallowWord = game.GallowWord.ToLower();
            if (msg == gallowWord)
            {
                var guessedGallow = game.members.Where(x => x.guessedGallow).Count();
                var points = General.GallowGuessPoints - guessedGallow + Time + (game.GallowWord.Length * General.GallowWordLengthMultiplierPlayer);
                gallowMember.guessedGallowTime = Time;
                gallowMember.gallowPoints += points > 0 ? points : 0;
                gallowMember.guessedGallow = true;

                ChatMessage correntAnswerServerMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"{message.username} answered correctly", color = Colors.SystemColor, usercolor = Colors.SystemUserColor };
                await _hub.Clients.GroupExcept(game.RoomId, sender.ConnectionId).sendmessage(correntAnswerServerMsg);
                ChatMessage correntAnswerPrivateMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"{message.username} you answered correct. You've been awarded {points} points", color = Colors.SystemColor, usercolor = Colors.SystemUserColor };
                await _hub.Clients.Client(sender.ConnectionId).sendmessage(correntAnswerPrivateMsg);
                if (game.members.Where(x => !x.isDrawing).All(x => x.guessedGallow))
                    await EndGallow(game, Time);

                await _hub.Clients.Group(game.RoomId).gallowusers(game.members);
            }
            else if (StringExtensions.CalculateWordDifference(msg, gallowWord) == 1)
            {
                ChatMessage closeMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"{message.username} {msg} was close!", color = Colors.SystemColor, usercolor = Colors.SystemUserColor };
                await _hub.Clients.Client(sender.ConnectionId).sendmessage(closeMsg);
            }
            else
            {
                await _hub.Clients.Group(game.RoomId).sendmessage(message);
            }
        }

        public async Task EndGallow(GallowLogic game, int Time)
        {
            var guessedGallow = game.members.Where(x => x.guessedGallow).ToList();
            game.drawings = new List<Drawing>();
            game.members.ForEach(x => { x.guessedGallow = false; });
            ChatMessage gallowEndedMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"Round has ended! The correct word was {game.GallowWord}", color = Colors.SystemColor, usercolor = Colors.SystemUserColor };
            await _hub.Clients.Group(game.RoomId).sendmessage(gallowEndedMsg);
            await _hub.Clients.Group(game.RoomId).whiteboardclear(true);
            game.UpdateGallowWord(false);

            int idx = game.members.FindIndex(x => x.isDrawing);
            if (idx == -1)
                await _hub.Clients.Group(game.RoomId).gallowusers(game.members);

            if (idx > -1)
            {
                var hostPoints = guessedGallow.Count();
                if (hostPoints > 0)
                {
                    hostPoints += (game.GallowWord.Length * General.GallowWordLengthMultiplierHost);
                    hostPoints += General.GallowDrawBasePoints;
                    hostPoints += (int)((double)guessedGallow.Sum(x => x.guessedGallowTime) / (double)guessedGallow.Count());
                    game.members[idx].gallowPoints += hostPoints > 0 ? hostPoints : 0;
                    ChatMessage hostMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"{game.members[idx].username} {guessedGallow.Count()} users got the word correct, good job. You've been awarded {hostPoints} points", color = Colors.SystemColor, usercolor = Colors.SystemUserColor };
                    await _hub.Clients.Client(game.members[idx].ConnectionId).sendmessage(hostMsg);
                }

                game.members[idx].isDrawing = false;
                await _hub.Clients.Client(game.members[idx].ConnectionId).isdrawingupdate(false);

                idx = (idx + 1) == game.members.Count ? 0 : idx + 1;
                game.members[idx].isDrawing = true;
                await _hub.Clients.Client(game.members[idx].ConnectionId).isdrawingupdate(true);
            }
        }
    }
}
