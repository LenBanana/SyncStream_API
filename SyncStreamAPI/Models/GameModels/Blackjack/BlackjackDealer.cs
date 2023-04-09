using SyncStreamAPI.Helper;
using System.Collections.Generic;
using System.Linq;

namespace SyncStreamAPI.Models.GameModels.Blackjack
{
    public class BlackjackDealer
    {
        public BlackjackDealer()
        {
            name = Games.Blackjack.BlackjackDealerNames.DealerName;
        }

        public string name { get; set; }
        public List<PlayingCard> cards { get; set; } = new List<PlayingCard>();
        public bool ShouldSerializepoints() { return false; }
        public int points => cards.CalculatePoints();
        public int? pointsDTO => cards?.Where(x => x.FaceUp).ToList().CalculatePoints();
        public double money { get; set; } = 0;
        public bool blackjack => (cards.Count == 2 && points == 21);
    }
}
