using SyncStreamAPI.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models.GameModels.Blackjack
{
    public class BlackjackDealer
    {
        public BlackjackDealer(string Name)
        {
            name = Name;
        }

        public string name { get; set; }
        public List<PlayingCard> cards { get; set; } = new List<PlayingCard>();
        public int points => cards.CalculatePoints();
        public bool blackjack => (cards.Count == 2 && points == 21);
    }
}
