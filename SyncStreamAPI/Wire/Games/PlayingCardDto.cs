using SyncStreamAPI.Enums.Games.Cards;
using SyncStreamAPI.Models.GameModels;

namespace SyncStreamAPI.Wire.Games;

/// <summary>
///     A card as a client is allowed to see it.
/// </summary>
/// <remarks>
///     A face-down card reveals nothing. Sending the domain <see cref="PlayingCard" /> shipped its suit and
///     rank whatever <c>FaceUp</c> said, so a player could read the dealer's hole card straight off the wire
///     while the table drew a card back. The dealer type already had a
///     <c>ShouldSerializepoints</c> hook hiding the derived total, so the intent to conceal was there; the
///     card itself was still telling.
/// </remarks>
public class PlayingCardDto
{
    public PlayingCardDto()
    {
    }

    public PlayingCardDto(PlayingCard card)
    {
        faceUp = card.FaceUp;
        if (!card.FaceUp) return;

        id = card.Id;
        suitS = card.SuitS;
        rankS = card.RankS;
        cardName = card.CardName;
        suit = card.Suit;
        rank = card.Rank;
    }

    public bool faceUp { get; set; }

    /// <summary>Null while the card is face down.</summary>
    public string? id { get; set; }

    public string? suitS { get; set; }
    public string? rankS { get; set; }
    public string? cardName { get; set; }
    public PlayingCardSuit? suit { get; set; }
    public PlayingCardRank? rank { get; set; }
}
