using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Blackjack;
using SyncStreamAPI.Models.GameModels.Members;
using SyncStreamAPI.ServerData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Games.Blackjack
{
    public class BlackjackManager
    {
        private readonly IHubContext<ServerHub, IServerHub> _hub;

        public static List<BlackjackLogic> blackjackGames { get; set; } = new List<BlackjackLogic>();

        public BlackjackManager(IHubContext<ServerHub, IServerHub> hub)
        {
            _hub = hub;
        }

        public void BlackjackEvents(BlackjackLogic game)
        {
            game.CardDealed += Game_CardDealed;
            game.DealerDealed += Game_DealerDealed;
            game.ShuffledDeck += Game_ShuffledDeck;
            game.RoundEnded += Game_RoundEnded;
        }

        private async void Game_RoundEnded(BlackjackLogic game)
        {
            await InitRound(game);
        }

        private async void Game_DealerDealed(BlackjackLogic game)
        {
            await _hub.Clients.Group(game.RoomId).sendblackjackdealer(game.dealer);
        }

        private void Game_ShuffledDeck(BlackjackLogic game)
        {

        }

        private async void Game_CardDealed(BlackjackLogic game, BlackjackMember member)
        {
            foreach (var _member in game.members)
            {
                await _hub.Clients.Client(_member.ConnectionId).sendblackjackself(_member);
                await _hub.Clients.Client(_member.ConnectionId).sendblackjackmembers(game.members.Where(x => x.ConnectionId != _member.ConnectionId).ToList());
            }
        }

        private async Task InitRound(BlackjackLogic game)
        {
            foreach (var member in game.members)
            {
                await _hub.Clients.Client(member.ConnectionId).sendblackjackself(member);
                await _hub.Clients.Client(member.ConnectionId).sendblackjackmembers(game.members.Where(x => x.ConnectionId != member.ConnectionId).ToList());
            }
            await _hub.Clients.Group(game.RoomId).sendblackjackdealer(game.dealer);
            await Task.Delay(2500);
            AskForBet(game, 0);
        }

        public async Task<bool> PlayNewRound(string UniqueId)
        {
            var idx = blackjackGames.FindIndex(x => x.RoomId == UniqueId);
            if (idx < 0)
            {
                Room room = DataManager.GetRoom(UniqueId);
                List<BlackjackMember> bjMember = new List<BlackjackMember>();
                foreach (var member in room.server.members)
                    bjMember.Add(member.ToBlackjackMember());

                var game = new BlackjackLogic(this, UniqueId, bjMember);
                blackjackGames.Add(game);
                await _hub.Clients.Group(UniqueId).playblackjack(true);
                await InitRound(game);
                return true;
            }
            await _hub.Clients.Group(UniqueId).playblackjack(false);
            blackjackGames.RemoveAt(idx);
            return false;
        }

        public async void AskForBet(BlackjackLogic game, int memberIdx)
        {
            if (memberIdx < game.members.Count)
                await _hub.Clients.Client(game.members[memberIdx].ConnectionId).askforbet();
            else
            {
                await game.PlayRound();
                await Task.Delay(2000);
                await game.PlayRound();
                await Task.Delay(2000);
                var idx = game.members.FindIndex(x => x.blackjack == false && x.points < 21);
                if (idx > -1)
                    AskForPull(game, idx);
                else
                    AskForPull(game, game.members.Count);
            }
        }

        public async void AskForPull(BlackjackLogic game, int memberIdx)
        {
            if (memberIdx < game.members.Count)
            {
                var member = game.members[memberIdx];
                var doubleOption = (member.cards.Count == 2);
                if (member.blackjack == false && member.points < 21)
                {
                    Console.WriteLine("Pull card " + member.ConnectionId);
                    await _hub.Clients.Client(member.ConnectionId).askforpull(doubleOption);
                    return;
                }
                AskForPull(game, memberIdx + 1);
            }
            else
                DealerPull(game);
        }

        public async void DealerPull(BlackjackLogic game)
        {
            game.dealer.cards[1].FaceUp = true;
            await _hub.Clients.Group(game.RoomId).sendblackjackdealer(game.dealer);
            await Task.Delay(1000);
            while (game.DealDealerCard())
            {
                await _hub.Clients.Group(game.RoomId).sendblackjackdealer(game.dealer);
                await Task.Delay(1000);
            }
            game.EndRound();
        }
    }
}
