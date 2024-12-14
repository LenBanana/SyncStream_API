using System;
using System.Collections.Generic;
using SyncStreamAPI.Enums.Games.Cards;

namespace SyncStreamAPI.Models.GameModels;

public class PlayingCardDeck
{
    public PlayingCardDeck()
    {
        foreach (PlayingCardSuit suit in Enum.GetValues(typeof(PlayingCardSuit)))
        foreach (PlayingCardRank rank in Enum.GetValues(typeof(PlayingCardRank)))
        {
            var nextCard = new PlayingCard(suit, rank);
            CardDeck.Add(nextCard);
        }
    }

    public List<PlayingCard> CardDeck { get; set; } = new();
}