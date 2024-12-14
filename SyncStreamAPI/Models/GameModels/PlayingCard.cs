using System.Linq;
using SyncStreamAPI.Enums.Games.Cards;

namespace SyncStreamAPI.Models.GameModels;

public class PlayingCard
{
    public PlayingCard(PlayingCardSuit suit, PlayingCardRank rank)
    {
        Suit = suit;
        Rank = rank;
    }

    public string Id => $"{Suit.ToString()}{Rank.ToString()}";
    public string SuitS => $"{Suit.ToString().First().ToString()}";
    public string RankS => GetRank();
    public string CardName => ToString();
    public PlayingCardSuit Suit { get; set; }
    public PlayingCardRank Rank { get; set; }
    public bool FaceUp { get; set; } = false;

    public int CardValue(int points)
    {
        switch (Rank)
        {
            case PlayingCardRank.Jack:
            case PlayingCardRank.Queen:
            case PlayingCardRank.King:
                return 10;
            case PlayingCardRank.Ace:
                return points > 10 ? 1 : 11;
            default:
                return (int)Rank;
        }
    }

    public string GetRank()
    {
        switch (Rank)
        {
            case PlayingCardRank.Jack:
            case PlayingCardRank.Queen:
            case PlayingCardRank.King:
            case PlayingCardRank.Ace:
                return Rank.ToString().First().ToString();
            default:
                return ((int)Rank).ToString();
        }
    }

    public override string ToString()
    {
        return $"{Rank.ToString()} of {Suit.ToString()}";
    }
}