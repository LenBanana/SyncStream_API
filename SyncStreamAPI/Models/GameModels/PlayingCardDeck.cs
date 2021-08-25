using SyncStreamAPI.Enums.Games.Cards;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models.GameModels
{
    public class PlayingCardDeck
    {
        public List<PlayingCard> CardDeck { get; set; } = new List<PlayingCard>();

        public PlayingCardDeck()
        {
            foreach (PlayingCardSuit colourPossibleValues in Enum.GetValues(typeof(PlayingCardSuit)))
            {
                foreach (PlayingCardRank namePossibleValues in Enum.GetValues(typeof(PlayingCardRank)))
                {
                    PlayingCard nextCard = new PlayingCard(colourPossibleValues, namePossibleValues);
                    CardDeck.Add(nextCard);
                }
            }
        }
    }
}
