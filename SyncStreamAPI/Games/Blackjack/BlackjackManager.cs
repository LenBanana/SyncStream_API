﻿using Microsoft.AspNetCore.SignalR;
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

        public void BlackjackGameEvents(BlackjackLogic game)
        {
            game.CardDealed += Game_CardDealed;
            game.DealerDealed += Game_DealerDealed;
            game.ShuffledDeck += Game_ShuffledDeck;
            game.RoundEnded += Game_RoundEnded;
        }

        public void BlackjackMemberEvents(BlackjackMember member)
        {
            member.DidSplit += Member_DidSplit;
        }

        private async void Member_DidSplit(BlackjackMember member)
        {
            var game = blackjackGames.FirstOrDefault(x => x.members.FindIndex(y => y.ConnectionId == member.ConnectionId) != -1);
            if (game != null)
            {
                await Task.Delay(1000);
                game.DealCard(member);
                await Task.Delay(1000);
                game.DealSplitCard(member);
                await Task.Delay(500);
                AskForSplitPull(game, game.members.FindIndex(x => x.ConnectionId == member.ConnectionId), false);
            }
        }

        private async void Game_RoundEnded(BlackjackLogic game)
        {
            await InitRound(game, 2000);
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
            await SendAllUsers(game);
        }

        private async Task InitRound(BlackjackLogic game, int timeout)
        {
            await SendAllUsers(game);
            game.members.ForEach(x => x.NewlyJoined = false);
            await Task.Delay(timeout);
            if (!game.GameEnded)
                AskForBet(game, 0);
        }

        public async Task SendAllUsers(BlackjackLogic game)
        {
            foreach (var member in game.members)
            {
                await _hub.Clients.Client(member.ConnectionId).sendblackjackself(member);
                await _hub.Clients.Client(member.ConnectionId).sendblackjackmembers(game.members.Where(x => x.ConnectionId != member.ConnectionId).ToList());
            }
            await _hub.Clients.Group(game.RoomId).sendblackjackdealer(game.dealer);
        }

        public async Task<bool> PlayNewRound(string UniqueId)
        {
            var idx = blackjackGames.FindIndex(x => x.RoomId == UniqueId);
            if (idx < 0)
            {
                Room room = DataManager.GetRoom(UniqueId);
                List<BlackjackMember> bjMember = new List<BlackjackMember>();
                foreach (var member in room.server.members)
                    bjMember.Add(member.ToBlackjackMember(this));

                var game = new BlackjackLogic(this, UniqueId, bjMember);
                blackjackGames.Add(game);
                await _hub.Clients.Group(UniqueId).playblackjack(true);
                await InitRound(game, 500);
                return true;
            }
            await _hub.Clients.Group(UniqueId).playblackjack(false);
            var _game = blackjackGames[idx];
            _game.CardDealed -= Game_CardDealed;
            _game.DealerDealed -= Game_DealerDealed;
            _game.ShuffledDeck -= Game_ShuffledDeck;
            _game.RoundEnded -= Game_RoundEnded;
            _game.GameEnded = true;
            foreach (var member in _game.members)
            {
                member.DidSplit -= Member_DidSplit;
            }
            blackjackGames.RemoveAt(idx);
            return false;
        }

        public async void AskForBet(BlackjackLogic game, int memberIdx)
        {
            if (memberIdx < game.members.Count && !game.members[memberIdx].NewlyJoined)
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
            if (memberIdx < game.members.Count && !game.members[memberIdx].NewlyJoined)
            {
                var member = game.members[memberIdx];
                var doubleOption = (member.cards.Count == 2);
                if (member.blackjack == false && member.points < 21)
                {
                    await _hub.Clients.Client(member.ConnectionId).askforpull(doubleOption);
                    return;
                }
                AskForPull(game, memberIdx + 1);
            }
            else
                DealerPull(game);
        }

        public async void AskForSplitPull(BlackjackLogic game, int memberIdx, bool pullForSplitHand)
        {
            var member = game.members[memberIdx];
            if (!pullForSplitHand)
            {
                if (member.blackjack == false && member.points < 21)
                    await _hub.Clients.Client(member.ConnectionId).askforsplitpull(pullForSplitHand);
                else
                    AskForSplitPull(game, memberIdx, !pullForSplitHand);
                return;
            }
            else
            {
                if (member.splitable == false && member.splitPoints < 21)
                {
                    await _hub.Clients.Client(member.ConnectionId).askforsplitpull(pullForSplitHand);
                    return;
                }
            }
            AskForPull(game, memberIdx + 1);
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
