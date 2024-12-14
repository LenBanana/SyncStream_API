using System.Collections.Generic;
using System.Linq;
using SyncStreamAPI.Models.GameModels;

namespace SyncStreamAPI.Helper;

public static class ListExtensions
{
    public static void Shuffle<T>(this IList<T> list)
    {
        var n = list.Count;
        while (n > 1)
        {
            n--;
            var k = General.Random.Next(n + 1);
            var value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    #region BlackjackCards

    public static void RemoveById(this List<PlayingCard> cards, string cardId)
    {
        var idx = cards.FindIndex(x => x.Id == cardId);
        if (idx > -1)
            cards.RemoveAt(idx);
        else
            throw new KeyNotFoundException("Could not find the specified card id");
    }

    public static int CalculatePoints(this List<PlayingCard> cards)
    {
        var points = 0;
        foreach (var card in cards.OrderBy(x => (int)x.Rank).ToList()) points += card.CardValue(points);

        return points;
    }

    public static void RemoveById(this List<PlayingCardDeck> cards, string cardId)
    {
        var idx = cards.FindIndex(x => x.CardDeck.FirstOrDefault(x => x.Id == cardId)?.Id == cardId);
        if (idx > -1)
        {
            var cardIdx = cards[idx].CardDeck.FindIndex(x => x.Id == cardId);
            if (cardIdx > -1)
            {
                cards[idx].CardDeck.RemoveAt(cardIdx);
                return;
            }
        }

        throw new KeyNotFoundException("Could not find the specified card id");
    }

    #endregion
}