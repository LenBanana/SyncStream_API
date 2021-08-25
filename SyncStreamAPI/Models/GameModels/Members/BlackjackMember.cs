using SyncStreamAPI.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models.GameModels.Members
{
    public class BlackjackMember
    {
        public BlackjackMember(string Username, string connectionId)
        {
            username = Username;
            ConnectionId = connectionId;
        }
        public bool ShouldSerializeConnectionId() { return false; }
        public string ConnectionId { get; set; }
        public string username { get; set; }
        public List<PlayingCard> cards { get; set; } = new List<PlayingCard>();
        public int points => cards.CalculatePoints();
        public double Money { get; set; } = 500;
        public double Bet { get; private set; } = 0;
        public bool blackjack => (cards.Count == 2 && points == 21);
        public bool doubled { get; set; } = false;

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
        }

        public void SetBet(double bet)
        {
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
