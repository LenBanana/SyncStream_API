using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static SyncStreamAPI.Models.Member;

namespace SyncStreamAPI.Models.GameModels.Members
{
    public class BlackjackMember
    {
        /// <summary>
        /// For normal members
        /// </summary>
        /// <param name="member"></param>
        /// <param name="manager"></param>
        public BlackjackMember(Member member, BlackjackManager manager)
        {
            username = member.username;
            ConnectionId = member.ConnectionId;
            member.Kicked += Member_Kicked;
            manager.BlackjackMemberEvents(this);
        }

        /// <summary>
        /// For Ai
        /// </summary>
        /// <param name="Username"></param>
        /// <param name="connectionId"></param>
        /// <param name="manager"></param>
        public BlackjackMember(string Username, string connectionId, BlackjackManager manager)
        {
            username = Username;
            ConnectionId = connectionId;
            manager.BlackjackMemberEvents(this);
        }

        private void Member_Kicked(Member e)
        {
            Kicked?.Invoke(e);
        }

        public event KickEvent Kicked;
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
        public bool notPlaying { get; set; } = false;
        public bool Ai { get; set; } = false;
        private bool _WaitingForBet { get; set; } = false;
        private bool _WaitingForPull { get; set; } = false;
        public bool waitingForBet
        {
            get { return _WaitingForBet; }
            set
            {
                _WaitingForBet = value;
                if (!value)
                    cancelWait.Cancel();
                else
                    WaitFor();
            }
        }
        public bool waitingForPull
        {
            get { return _WaitingForPull; }
            set
            {
                _WaitingForPull = value;
                if (value == false)
                    cancelWait.Cancel();
                else
                    WaitFor();
            }
        }
        private CancellationTokenSource cancelWait { get; set; } = new CancellationTokenSource();

        public delegate void MemberEvent(BlackjackMember member);
        public event MemberEvent DidSplit;
        public event MemberEvent FailedToReact;

        private async void WaitFor()
        {
            cancelWait = new CancellationTokenSource();
            await Task.Delay(60000, cancelWait.Token).ContinueWith(task =>
            {
                if (waitingForBet || waitingForPull)
                    FailedToReact?.Invoke(this);
            });
        }

        public double AddMoney(int dealerPoints)
        {
            var money = Money;
            if ((dealerPoints < points || dealerPoints > 21) && points <= 21)
            {
                if (blackjack)
                    Money = Money + Bet * 2.5;
                else if (doubled)
                    Money = Money + (Bet * 2) * 2;
                else
                    Money = Money + Bet * 2;
            }
            else if (dealerPoints == points)
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
                else if (dealerPoints == splitPoints)
                    Money = Money + Bet;
            }
            if (money == Money)
                return 0;
            else if (Money == (money + Bet))
                return Bet;
            else
                return Money - money;
        }

        public void SetBet(double bet)
        {
            waitingForBet = false;
            Money = Money - bet;
            Bet = bet;
        }

        public void DoubleBet()
        {
            Money = Money - Bet;
            Bet = Bet * 2;
        }
    }
}
