using System;
using System.Collections.Generic;
using SyncStreamAPI.Enums.Games.Cards;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models.GameModels.Members;

namespace SyncStreamAPI.Models.GameModels.Durak;

public class DurakLogic
{
    public DurakLogic()
    {
        BuildDeck();
    }

    public List<PlayingCard> cardDeck { get; set; } = new();
    public List<DurakMember> members { get; set; } = new();

    public void BuildDeck()
    {
        foreach (PlayingCardSuit suit in Enum.GetValues(typeof(PlayingCardSuit)))
        foreach (PlayingCardRank rank in Enum.GetValues(typeof(PlayingCardRank)))
            if ((int)rank > 5)
            {
                var nextCard = new PlayingCard(suit, rank);
                cardDeck.Add(nextCard);
            }

        cardDeck.Shuffle();
        GiveCardsToMembers();
    }

    public void GiveCardsToMembers()
    {
        foreach (var member in members)
            for (var i = member.cards.Count; i < 6; i++)
            {
                member.cards.Add(cardDeck[i]);
                cardDeck.RemoveAt(i);
            }
    }
}