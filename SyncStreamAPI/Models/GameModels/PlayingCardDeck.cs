using SyncStreamAPI.Enums.Games.Cards;
using System;
using System.Collections.Generic;

namespace SyncStreamAPI.Models.GameModels
{
    public class PlayingCardDeck
    {
        public List<PlayingCard> CardDeck { get; set; } = new List<PlayingCard>();

        public PlayingCardDeck()
        {
            foreach (PlayingCardSuit suit in Enum.GetValues(typeof(PlayingCardSuit)))
            {
                foreach (PlayingCardRank rank in Enum.GetValues(typeof(PlayingCardRank)))
                {
                    PlayingCard nextCard = new PlayingCard(suit, rank);
                    CardDeck.Add(nextCard);
                }
            }
        }
    }
}
