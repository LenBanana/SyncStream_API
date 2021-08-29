using System;
using System.Collections.Generic;
using SyncStreamAPI.Helper;
using System.Linq;
using SyncStreamAPI.Models.GameModels.Members;
using SyncStreamAPI.Games.Blackjack;
using System.Threading.Tasks;
using SyncStreamAPI.Enums.Games.Cards;
using SyncStreamAPI.Enums.Games;

namespace SyncStreamAPI.Models.GameModels.Blackjack
{
    public class BlackjackLogic
    {
        public string RoomId { get; set; }
        public List<PlayingCardDeck> playingCardDecks { get; set; } = new List<PlayingCardDeck>();
        public List<PlayingCard> playingCards { get; set; } = new List<PlayingCard>();
        public List<BlackjackMember> members { get; set; } = new List<BlackjackMember>();
        public BlackjackDealer dealer { get; set; } = new BlackjackDealer();
        public bool GameEnded { get; set; } = false;

        public delegate void BlackjackGameEvent(BlackjackLogic game);
        public delegate void CardDealEvent(BlackjackLogic game, BlackjackMember member);
        public event CardDealEvent CardDealed;
        public event BlackjackGameEvent DealerDealed;
        public event BlackjackGameEvent ShuffledDeck;
        public event BlackjackGameEvent RoundEnded;

        public BlackjackLogic(BlackjackManager manager, string roomId, List<BlackjackMember> Members)
        {
            RoomId = roomId;
            members = Members;
            manager.BlackjackGameEvents(this);
            ResetBlackjackDeck();
        }

        public void ResetBlackjackDeck()
        {
            playingCardDecks = new List<PlayingCardDeck>();
            for (int i = 0; i < General.BlackjackShoeSize; i++)
                playingCardDecks.Add(new PlayingCardDeck());
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

        public (List<PlayingCard> membersCards, List<PlayingCard> otherCards, List<PlayingCard> dealerCards) GetAllDecks(string ConnectionId)
        {
            var mCards = new List<PlayingCard>();
            var oCards = new List<PlayingCard>();
            var dCards = dealer.cards;

            var idx = members.FindIndex(x => x.ConnectionId == ConnectionId);
            if (idx > -1)
                mCards = members[idx].cards;

            for (int i = 0; i < members.Count; i++)
                if (i != idx)
                    oCards.AddRange(members[i].cards);

            return (mCards, oCards, dCards);
        }

        public async Task PlayRound()
        {
            foreach (var member in members.ToList())
            {
                DealCard(member);
                await Task.Delay(500);
            }
            DealDealerCard();
        }

        public bool DealCard(BlackjackMember member)
        {
            if (member.NewlyJoined||member.notPlaying)
                return false;
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
                if (dealer.cards.Count != 1)
                    dealersCard.FaceUp = true;
                dealer.cards.Add(dealersCard);
                DealerDealed?.Invoke(this);
                return true;
            }
            return false;
        }

        public void CheckDeckSize()
        {
            var remainingSize = playingCards.Count();
            if (remainingSize < (General.BlackjackShoeSize * 52 / 2))
                ResetBlackjackDeck();
        }

        public async void EndRound()
        {
            await Task.Delay(1500);
            RoundEnded?.Invoke(this);
            CheckDeckSize();
        }
    }
}
