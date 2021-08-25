using System;
using System.Collections.Generic;
using SyncStreamAPI.Helper;
using System.Linq;
using SyncStreamAPI.Models.GameModels.Members;
using SyncStreamAPI.Games.Blackjack;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models.GameModels.Blackjack
{
    public class BlackjackLogic
    {
        public string RoomId { get; set; }
        public List<PlayingCardDeck> playingCardDecks { get; set; } = new List<PlayingCardDeck>();
        public List<PlayingCard> playingCards { get { return playingCardDecks.SelectMany(x => x.CardDeck).ToList(); } }
        public List<BlackjackMember> members { get; set; } = new List<BlackjackMember>();
        public BlackjackDealer dealer { get; set; } = new BlackjackDealer("Fred");

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
            manager.BlackjackEvents(this);
            ResetBlackjackDeck();
        }

        public void ResetBlackjackDeck()
        {
            playingCardDecks = new List<PlayingCardDeck>();
            for (int i = 0; i < General.BlackjackShoeSize; i++)
                playingCardDecks.Add(new PlayingCardDeck());
            Shuffle();
        }

        public void Shuffle()
        {
            playingCardDecks.ForEach(x => x.CardDeck.Shuffle());
            ShuffledDeck?.Invoke(this);
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
            foreach (var member in members)
            {
                DealCard(member);
                await Task.Delay(500);
            }
            DealDealerCard();
        }

        public void CheckDeckSize()
        {
            var remainingSize = playingCards.Count();
            if (remainingSize < (General.BlackjackShoeSize * 52 / 2))
                ResetBlackjackDeck();
        }

        public bool DealDealerCard()
        {
            if (dealer.points < 17)
            {
                var dealersCard = playingCards[0];
                if (dealer.cards.Count != 0)
                    dealersCard.FaceUp = true;
                dealer.cards.Add(dealersCard);
                playingCardDecks.RemoveById(dealersCard.Id);
                DealerDealed?.Invoke(this);
                return true;
            }
            return false;
        }

        public void DealCard(BlackjackMember member)
        {
            var card = playingCards[0];
            card.FaceUp = true;
            member.cards.Add(card);
            playingCardDecks.RemoveById(card.Id);
            CardDealed?.Invoke(this, member);
        }

        public void EndRound()
        {
            foreach (var member in members)
            {
                member.AddMoney(dealer.points);
                member.cards = new List<PlayingCard>();
                CardDealed?.Invoke(this, member);
            }
            dealer.cards = new List<PlayingCard>();
            DealerDealed?.Invoke(this);
            CheckDeckSize();
            RoundEnded?.Invoke(this);
        }
    }
}
