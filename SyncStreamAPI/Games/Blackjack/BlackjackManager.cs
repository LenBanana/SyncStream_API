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
            foreach (var member in game.members)
                await _hub.Clients.Client(member.ConnectionId).sendblackjackmembers(member, game.members.Where(x => x.UserId != member.UserId).ToList());
            await _hub.Clients.Group(game.RoomId).sendblackjackdealer(game.dealer);
            await Task.Delay(2500);
            AskForBet(game, 0);
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
            await _hub.Clients.Client(member.ConnectionId).sendblackjackmembers(member, game.members.Where(x => x.UserId != member.UserId).ToList());
        }

        public bool PlayNewRound(string UniqueId)
        {
            var idx = blackjackGames.FindIndex(x => x.RoomId == UniqueId);
            if (idx < 0)
            {
                Room room = DataManager.GetRoom(UniqueId);
                List<BlackjackMember> bjMember = new List<BlackjackMember>();
                foreach (var member in room.server.members)
                    bjMember.Add(member.ToBlackjackMember(bjMember.Count));

                var game = new BlackjackLogic(this, UniqueId, bjMember);
                blackjackGames.Add(game);
                AskForBet(game, 0);
                return true;
            }
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
                await Task.Delay(1000);
                AskForPull(game, game.members.FindIndex(x => x.blackjack == false));
            }
        }

        public async void AskForPull(BlackjackLogic game, int memberIdx)
        {
            if (memberIdx < game.members.Count)
            {
                var member = game.members[memberIdx];
                if (member.blackjack == false)
                    await _hub.Clients.Client(member.ConnectionId).askforpull(member.cards.Count == 2);
                else
                    AskForPull(game, memberIdx++);
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
