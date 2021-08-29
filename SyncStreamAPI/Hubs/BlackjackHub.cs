﻿using SyncStreamAPI.Models.GameModels.Members;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task PlayBlackjack(string UniqueId)
        {
            var playing = await _blackjackManager.PlayNewRound(UniqueId);
            var room = GetRoom(UniqueId);
            if (playing && room.GallowGame != null)
                _gallowGameManager.PlayNewRound(UniqueId);
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
                var bjMember = new BlackjackMember($"BlackJack-Ai {Helper.General.random.Next(0, 99)}", "", _blackjackManager);
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
                if (member.waitingForBet)
                {
                    member.waitingForBet = false;
                    member.SetBet(5);
                    _blackjackManager.AskForBet(game, idx + 1);
                }
                if (member.waitingForPull)
                {
                    member.waitingForPull = false;
                    await Task.Delay(500);
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
                await Task.Delay(500);
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
            await Task.Delay(500);
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
            await Task.Delay(500);
            if (pull)
            {
                if (pullForSplitHand == true)
                    game.DealSplitCard(member);
                else
                    game.DealCard(member);
                _blackjackManager.AskForSplitPull(game, idx, pullForSplitHand);
            }
            else
            {
                if (pullForSplitHand == false)
                    _blackjackManager.AskForSplitPull(game, idx, true);
                else
                    _blackjackManager.AskForPull(game, idx + 1);
            }
        }
    }
}
