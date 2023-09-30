using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Members;
using System;
using System.Threading.Tasks;
using SyncStreamAPI.ServerData.Helper;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task PlayBlackjack(string UniqueId)
        {
            var playing = await _blackjackManager.PlayNewRound(UniqueId);
            var room = GetRoom(UniqueId);
            if (playing && room.GallowGame != null)
            {
                _gallowGameManager.PlayNewRound(UniqueId);
                await RoomManager.SendPlayerType(room);
            }
            else
            {
                await Clients.Group(UniqueId).playertype(PlayerType.Blackjack);
            }
        }

        public async Task SetBet(string UniqueId, double bet)
        {
            var room = GetRoom(UniqueId);
            var game = room.BlackjackGame;
            var idx = game.members.FindIndex(x => x.ConnectionId == Context.ConnectionId);
            var member = game.members[idx];
            member.SetBet(bet);
            game.dealer.money += member.Bet;
            await _blackjackManager.SendAllUsers(game);
            _blackjackManager.AskForBet(game, idx + 1);
        }

        public async Task SpectateBlackjack(string UniqueId)
        {
            var room = GetRoom(UniqueId);
            var game = room.BlackjackGame;
            if (game != null && game.members.Count > 1)
            {
                var idx = game.members.FindIndex(x => x.ConnectionId == Context.ConnectionId);
                var member = game.members[idx];
                if (game.members.Count < 5 && member.notPlaying)
                {
                    member.NewlyJoined = true;
                    member.notPlaying = false;
                }
                else
                {
                    if (member.waitingForBet)
                    {
                        member.waitingForBet = false;
                        _blackjackManager.AskForBet(game, idx + 1);
                    }
                    if (member.waitingForPull)
                    {
                        member.waitingForPull = false;
                        _blackjackManager.AskForPull(game, idx + 1);
                    }
                    member.notPlaying = true;
                }
                await _blackjackManager.SendAllUsers(game);
            }
        }

        public async Task AddBlackjackAi(string UniqueId)
        {
            var room = GetRoom(UniqueId);
            var game = room.BlackjackGame;
            if (game != null && game.members.Count < 5)
            {
                var bjMember = new BlackjackMember($"BlackJack-Ai {Helper.General.Random.Next(0, 99)}", "", _blackjackManager);
                bjMember.NewlyJoined = true;
                bjMember.Ai = true;
                game.members.Add(bjMember);
                await _blackjackManager.SendAllUsers(game);
            }
        }

        public async Task MakeAi(string UniqueId)
        {
            var room = GetRoom(UniqueId);
            var game = room.BlackjackGame;
            if (game != null)
            {
                var idx = game.members.FindIndex(x => x.ConnectionId == Context.ConnectionId);
                var member = game.members[idx];
                member.Ai = !member.Ai;
                var errorMsg = new SystemMessage($"{(member.Ai ? "Activated" : "Deactivated")} AI");
                await Clients.Caller.sendmessage(errorMsg);
                if (member.waitingForBet)
                {
                    member.waitingForBet = false;
                    member.SetBet(5);
                    _blackjackManager.AskForBet(game, idx + 1);
                }
                if (member.waitingForPull)
                {
                    member.waitingForPull = false;
                    await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
                    _blackjackManager.AskForPull(game, idx);
                }
            }
            await _blackjackManager.SendAllUsers(game);
        }

        public async Task TakePull(string UniqueId, bool pull, bool doubleOption, bool splitOption)
        {
            var room = GetRoom(UniqueId);
            var game = room.BlackjackGame;
            var idx = game.members.FindIndex(x => x.ConnectionId == Context.ConnectionId);
            var member = game.members[idx];
            member.waitingForPull = false;
            await _blackjackManager.SendAllUsers(game);
            if (pull)
            {
                game.DealCard(member);
                await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
                _blackjackManager.AskForPull(game, idx);
                return;
            }
            else if (doubleOption)
            {
                member.DoubleBet();
                game.dealer.money += member.Bet;
                game.DealCard(member);
            }
            else if (splitOption)
            {
                member.didSplit = splitOption;
                return;
            }
            await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
            _blackjackManager.AskForPull(game, idx + 1);
        }

        public async Task TakeSplitPull(string UniqueId, bool pull, bool pullForSplitHand)
        {
            var room = GetRoom(UniqueId);
            var game = room.BlackjackGame;
            var idx = game.members.FindIndex(x => x.ConnectionId == Context.ConnectionId);
            var member = game.members[idx];
            member.waitingForPull = false;
            await _blackjackManager.SendAllUsers(game);
            await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
            if (pull)
            {
                if (pullForSplitHand == true)
                {
                    game.DealSplitCard(member);
                }
                else
                {
                    game.DealCard(member);
                }

                _blackjackManager.AskForSplitPull(game, idx, pullForSplitHand);
            }
            else
            {
                if (pullForSplitHand == false)
                {
                    _blackjackManager.AskForSplitPull(game, idx, true);
                }
                else
                {
                    _blackjackManager.AskForPull(game, idx + 1);
                }
            }
        }
    }
}
