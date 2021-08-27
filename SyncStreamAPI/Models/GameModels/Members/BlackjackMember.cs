using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models.GameModels.Members
{
    public class BlackjackMember
    {
        public BlackjackMember(string Username, string connectionId, BlackjackManager manager)
        {
            username = Username;
            ConnectionId = connectionId;
            manager.BlackjackMemberEvents(this);
        }
        public bool ShouldSerializeConnectionId() { return false; }
        public string ConnectionId { get; set; }
        public string username { get; set; }
        public List<PlayingCard> cards { get; set; } = new List<PlayingCard>();
        public List<PlayingCard> splitCards { get; set; } = new List<PlayingCard>();
        public int points => cards.CalculatePoints();
        public int splitPoints => splitCards.CalculatePoints();
        public double Money { get; set; } = 500;
        public double Bet { get; private set; } = 0;
        public bool splitable => (cards.Count == 2 && cards[0].Rank == cards[1].Rank);
        private bool _didSplit { get; set; } = false;
        public bool didSplit { get { return _didSplit; } set { _didSplit = value; if (value) { Money = Money - Bet; splitCards.Add(cards[1]); cards.RemoveAt(1); DidSplit?.Invoke(this); } else { splitCards = new List<PlayingCard>(); } } }
        public bool blackjack => (cards.Count == 2 && points == 21);
        public bool splitBlackjack => (splitCards.Count == 2 && splitPoints == 21);
        public bool doubled { get; set; } = false;
        public bool NewlyJoined { get; set; } = true;

        public delegate void DidSplitEvent(BlackjackMember member);
        public event DidSplitEvent DidSplit;

        public void AddMoney(int dealerPoints)
        {
            if ((dealerPoints < points || dealerPoints > 21) && points <= 21)
            {
                if (blackjack)
                    Money = Money + Bet * 2.5;
                else if (doubled)
                    Money = Money + (Bet * 2) * 2;
                else
                    Money = Money + Bet * 2;
            }
            else if (dealerPoints < 21 && dealerPoints == points)
                Money = Money + Bet;
            if (didSplit)
            {
                if ((dealerPoints < splitPoints || dealerPoints > 21) && splitPoints <= 21)
                {
                    if (splitBlackjack)
                        Money = Money + Bet * 2.5;
                    else
                        Money = Money + Bet * 2;
                }
                else if (dealerPoints < 21 && dealerPoints == splitPoints)
                    Money = Money + Bet;
            }
        }

        public void SetBet(double bet)
        {
            didSplit = false;
            Money = Money - bet;
            Bet = bet;
        }

        public void doubleBet()
        {
            Money = Money - Bet;
            Bet = Bet * 2;
        }
    }
}
