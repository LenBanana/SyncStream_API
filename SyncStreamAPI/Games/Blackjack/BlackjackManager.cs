﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels;
using SyncStreamAPI.Models.GameModels.Blackjack;
using SyncStreamAPI.Models.GameModels.Members;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Games.Blackjack;

public class BlackjackManager
{
    private readonly IHubContext<ServerHub, IServerHub> _hub;

    public BlackjackManager(IHubContext<ServerHub, IServerHub> hub)
    {
        _hub = hub;
    }

    public static List<BlackjackLogic> blackjackGames { get; set; } = new();

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
        member.FailedToReact += Member_FailedToReact;
    }

    private void Member_FailedToReact(BlackjackMember member)
    {
        var game = blackjackGames.FirstOrDefault(x =>
            x.members.FindIndex(y => y.ConnectionId == member.ConnectionId) != -1);
        if (game != null)
        {
            var memberIdx = game.members.FindIndex(x => x.ConnectionId == member.ConnectionId);
            if (member.waitingForBet && !member.NewlyJoined && !member.notPlaying)
            {
                member.SetBet(5);
                game.dealer.money += member.Bet;
                AskForBet(game, memberIdx + 1);
                member.waitingForBet = false;
            }
            else if (member.waitingForPull && !member.NewlyJoined && !member.notPlaying)
            {
                AskForPull(game, memberIdx + 1);
                member.waitingForPull = false;
            }
        }
    }

    private async void Member_DidSplit(BlackjackMember member)
    {
        var game = blackjackGames.FirstOrDefault(x =>
            x.members.FindIndex(y => y.ConnectionId == member.ConnectionId) != -1);
        if (game != null)
        {
            await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2500));
            game.DealCard(member);
            await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2500));
            game.DealSplitCard(member);
            await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
            AskForSplitPull(game, game.members.FindIndex(x => x.ConnectionId == member.ConnectionId), false);
        }
    }

    private async void Game_RoundEnded(BlackjackLogic game)
    {
        await AddMoney(game);
        await InitRound(game, 1500);
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

    private async Task AddMoney(BlackjackLogic game)
    {
        var dealerText = $"Dealer had {game.dealer.pointsDTO}. ";
        foreach (var member in game.members?.Where(x => !x.notPlaying && !x.NewlyJoined).ToList())
        {
            var totalText = $"You had {member.points}";

            if (member.didSplit)
                totalText += $" on your main hand. {member.splitPoints} on your split hand. ";
            else
                totalText += ". ";

            totalText += dealerText;
            var money = member.AddMoney(game.dealer.points);
            if (money > 0)
            {
                game.dealer.money -= money;
                if (money > member.Bet)
                    totalText += "Congratulations you won.";
                else
                    totalText += "Pushed, you got your money back.";
            }
            else
            {
                totalText += "Better luck next time.";
            }

            member.cards = new List<PlayingCard>();
            member.didSplit = false;
            var roundEndMsg = new SystemMessage(totalText);
            await _hub.Clients.Client(member.ConnectionId).sendmessage(roundEndMsg);
        }

        game.dealer.cards = new List<PlayingCard>();
    }

    private async Task InitRound(BlackjackLogic game, int timeout)
    {
        await SendAllUsers(game);
        game.members.ForEach(x => x.NewlyJoined = false);
        await Task.Delay(timeout);
        if (!game.GameEnded) AskForBet(game, 0);
    }

    public async Task SendAllUsers(BlackjackLogic game)
    {
        foreach (var member in game.members?.Where(x => x.ConnectionId.Length > 0).ToList())
        {
            await _hub.Clients.Client(member.ConnectionId).sendblackjackself(member);
            await _hub.Clients.Client(member.ConnectionId)
                .sendblackjackmembers(game.members?.Where(x => x.ConnectionId != member.ConnectionId).ToList());
        }

        await _hub.Clients.Group(game.RoomId).sendblackjackdealer(game.dealer);
    }

    public async Task<bool> PlayNewRound(string UniqueId)
    {
        var idx = blackjackGames.FindIndex(x => x.RoomId == UniqueId);
        if (idx < 0)
        {
            var room = MainManager.GetRoom(UniqueId);
            var bjMember = new List<BlackjackMember>();
            foreach (var member in room.server.members.Take(5).ToList()) bjMember.Add(member.ToBlackjackMember(this));

            if (room.server.members.Count > 5)
                foreach (var member in room.server.members.Skip(5).ToList())
                {
                    var bjMem = member.ToBlackjackMember(this);
                    bjMem.notPlaying = true;
                    bjMember.Add(bjMem);
                }

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
            member.waitingForBet = false;
            member.waitingForPull = false;
            member.DidSplit -= Member_DidSplit;
            member.FailedToReact -= Member_FailedToReact;
        }

        blackjackGames.RemoveAt(idx);
        return false;
    }

    public async void AskForBet(BlackjackLogic game, int memberIdx)
    {
        if (memberIdx < game.members.Count)
        {
            var member = game.members[memberIdx];
            if (!member.notPlaying && !member.NewlyJoined)
            {
                if (!member.Ai)
                {
                    await _hub.Clients.Client(member.ConnectionId).askforbet();
                    member.waitingForBet = true;
                    await SendAllUsers(game);
                }
                else
                {
                    await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(1250));
                    member.SetBet(5);
                    game.dealer.money += member.Bet;
                    AskForBet(game, memberIdx + 1);
                }
            }
            else
            {
                AskForBet(game, memberIdx + 1);
            }
        }
        else
        {
            await game.PlayRound();
            await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2500));
            await game.PlayRound();
            await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2500));
            var idx = game.members.FindIndex(x =>
                !x.notPlaying && !x.NewlyJoined && x.blackjack == false && x.points < 21);
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
            var doubleOption = member.cards.Count == 2;
            if (!member.notPlaying && !member.NewlyJoined && member.blackjack == false && member.points < 21)
            {
                if (!member.Ai)
                {
                    await _hub.Clients.Client(member.ConnectionId).askforpull(doubleOption);
                    member.waitingForPull = true;
                    await SendAllUsers(game);
                }
                else
                {
                    await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
                    switch (BlackjackAi.SmartPull(member, game.dealer, true, false))
                    {
                        case BlackjackSmartReaction.Stand:
                            AskForPull(game, memberIdx + 1);
                            break;
                        case BlackjackSmartReaction.Hit:
                            game.DealCard(member);
                            AskForPull(game, memberIdx);
                            break;
                        case BlackjackSmartReaction.Double:
                            member.DoubleBet();
                            game.dealer.money += member.Bet;
                            game.DealCard(member);
                            AskForPull(game, memberIdx + 1);
                            break;
                        case BlackjackSmartReaction.Split:
                            member.didSplit = true;
                            break;
                    }
                }

                return;
            }

            AskForPull(game, memberIdx + 1);
        }
        else
        {
            DealerPull(game);
        }
    }

    public async void AskForSplitPull(BlackjackLogic game, int memberIdx, bool pullForSplitHand)
    {
        var member = game.members[memberIdx];
        if (pullForSplitHand == false)
        {
            if (member.points < 21)
            {
                if (!member.Ai)
                {
                    await _hub.Clients.Client(member.ConnectionId).askforsplitpull(pullForSplitHand);
                    member.waitingForPull = true;
                    await SendAllUsers(game);
                }
                else
                {
                    await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
                    switch (BlackjackAi.SmartPull(member, game.dealer, false, pullForSplitHand))
                    {
                        case BlackjackSmartReaction.Stand:
                            AskForSplitPull(game, memberIdx, true);
                            break;
                        case BlackjackSmartReaction.Hit:
                            game.DealCard(member);
                            AskForSplitPull(game, memberIdx, false);
                            break;
                    }
                }
            }
            else
            {
                AskForSplitPull(game, memberIdx, true);
            }

            return;
        }

        if (member.splitPoints < 21)
        {
            if (!member.Ai)
            {
                await _hub.Clients.Client(member.ConnectionId).askforsplitpull(pullForSplitHand);
                member.waitingForPull = true;
                await SendAllUsers(game);
            }
            else
            {
                await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
                switch (BlackjackAi.SmartPull(member, game.dealer, false, pullForSplitHand))
                {
                    case BlackjackSmartReaction.Stand:
                        AskForPull(game, memberIdx + 1);
                        break;
                    case BlackjackSmartReaction.Hit:
                        game.DealSplitCard(member);
                        AskForSplitPull(game, memberIdx, true);
                        break;
                }
            }

            return;
        }

        AskForPull(game, memberIdx + 1);
    }

    public async void DealerPull(BlackjackLogic game)
    {
        game.dealer.cards[1].FaceUp = true;
        await _hub.Clients.Group(game.RoomId).sendblackjackdealer(game.dealer);
        await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2500));
        while (game.DealDealerCard())
        {
            await _hub.Clients.Group(game.RoomId).sendblackjackdealer(game.dealer);
            await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2500));
        }

        game.EndRound();
    }
}