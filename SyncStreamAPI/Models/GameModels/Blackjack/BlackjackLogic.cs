using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models.GameModels.Members;

namespace SyncStreamAPI.Models.GameModels.Blackjack;

public class BlackjackLogic
{
    public delegate void BlackjackGameEvent(BlackjackLogic game);

    public delegate void CardDealEvent(BlackjackLogic game, BlackjackMember member);

    public BlackjackLogic(BlackjackManager manager, string roomId, List<BlackjackMember> Members)
    {
        RoomId = roomId;
        members = Members;
        members.ForEach(x => x.Kicked += X_Kicked);
        manager.BlackjackGameEvents(this);
        ResetBlackjackDeck();
    }

    public string RoomId { get; set; }
    public List<PlayingCardDeck> playingCardDecks { get; set; } = new();
    public List<PlayingCard> playingCards { get; set; } = new();
    public List<BlackjackMember> members { get; set; } = new();
    public BlackjackDealer dealer { get; set; } = new();
    public bool GameEnded { get; set; } = false;
    public event CardDealEvent CardDealed;
    public event BlackjackGameEvent DealerDealed;
    public event BlackjackGameEvent ShuffledDeck;
    public event BlackjackGameEvent RoundEnded;

    private void X_Kicked(Member e)
    {
        var idx = members.FindIndex(x => x.ConnectionId == e.ConnectionId);
        if (idx >= 0)
        {
            e.Kicked -= X_Kicked;
            members.RemoveAt(idx);
        }
    }

    public void AddMember(Member member, BlackjackManager manager)
    {
        member.Kicked += X_Kicked;
        var newMember = new BlackjackMember(member, manager);
        if (members.Count >= 5) newMember.notPlaying = true;

        members.Add(newMember);
    }

    public void ResetBlackjackDeck()
    {
        playingCardDecks = new List<PlayingCardDeck>();
        for (var i = 0; i < General.BlackjackShoeSize; i++) playingCardDecks.Add(new PlayingCardDeck());

        playingCards = playingCardDecks.SelectMany(x => x.CardDeck).ToList();
        Shuffle();
    }

    public void Shuffle()
    {
        playingCards.Shuffle();
        ShuffledDeck?.Invoke(this);
    }

    public PlayingCard PullCard()
    {
        var card = playingCards[0];
        playingCards.RemoveAt(0);
        return card;
    }

    public (List<PlayingCard> membersCards, List<PlayingCard> otherCards, List<PlayingCard> dealerCards) GetAllDecks(
        string ConnectionId)
    {
        var mCards = new List<PlayingCard>();
        var oCards = new List<PlayingCard>();
        var dCards = dealer.cards;

        var idx = members.FindIndex(x => x.ConnectionId == ConnectionId);
        if (idx > -1) mCards = members[idx].cards;

        for (var i = 0; i < members.Count; i++)
            if (i != idx)
                oCards.AddRange(members[i].cards);

        return (mCards, oCards, dCards);
    }

    public async Task PlayRound()
    {
        foreach (var member in members.ToList())
        {
            DealCard(member);
            await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
        }

        DealDealerCard();
    }

    public bool DealCard(BlackjackMember member)
    {
        if (member.NewlyJoined || member.notPlaying) return false;

        var card = PullCard();
        card.FaceUp = true;
        member.cards.Add(card);
        CardDealed?.Invoke(this, member);
        return true;
    }

    public bool DealSplitCard(BlackjackMember member)
    {
        var card = PullCard();
        card.FaceUp = true;
        member.splitCards.Add(card);
        CardDealed?.Invoke(this, member);
        return true;
    }

    public bool DealDealerCard()
    {
        if (dealer.points < 17)
        {
            var dealersCard = PullCard();
            if (dealer.cards.Count != 1) dealersCard.FaceUp = true;

            dealer.cards.Add(dealersCard);
            DealerDealed?.Invoke(this);
            return true;
        }

        return false;
    }

    public void CheckDeckSize()
    {
        var remainingSize = playingCards.Count();
        if (remainingSize < General.BlackjackShoeSize * 52 / 2) ResetBlackjackDeck();
    }

    public async void EndRound()
    {
        await BlackjackTimer.RndDelay(TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(3000));
        RoundEnded?.Invoke(this);
        CheckDeckSize();
    }
}