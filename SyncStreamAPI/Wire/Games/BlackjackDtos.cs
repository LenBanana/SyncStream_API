using System.Collections.Generic;
using System.Linq;
using SyncStreamAPI.Models.GameModels.Blackjack;
using SyncStreamAPI.Models.GameModels.Members;

namespace SyncStreamAPI.Wire.Games;

/// <summary>
///     A blackjack player as a client sees them.
/// </summary>
/// <remarks>
///     The domain <see cref="BlackjackMember" /> carries a connection id, money-handling methods and event
///     subscriptions to the room member's lifecycle. It kept the connection id off the wire with a
///     <c>ShouldSerializeConnectionId</c> hook, which worked but is one rename away from silently exposing
///     it again. Here the field simply is not present.
/// </remarks>
public class BlackjackMemberDto
{
    public BlackjackMemberDto()
    {
    }

    public BlackjackMemberDto(BlackjackMember member)
    {
        username = member.username;
        cards = member.cards.Select(card => new PlayingCardDto(card)).ToList();
        splitCards = member.splitCards.Select(card => new PlayingCardDto(card)).ToList();
        points = member.points;
        splitPoints = member.splitPoints;
        money = member.Money;
        bet = member.Bet;
        blackjack = member.blackjack;
        splitBlackjack = member.splitBlackjack;
        didSplit = member.didSplit;
        splitable = member.splitable;
        waitingForBet = member.waitingForBet;
        waitingForPull = member.waitingForPull;
        notPlaying = member.notPlaying;
        doubled = member.doubled;
    }

    public string? username { get; set; }
    public List<PlayingCardDto> cards { get; set; } = new();
    public List<PlayingCardDto> splitCards { get; set; } = new();
    public int points { get; set; }
    public int splitPoints { get; set; }
    public double money { get; set; }
    public double bet { get; set; }
    public bool blackjack { get; set; }
    public bool splitBlackjack { get; set; }
    public bool didSplit { get; set; }
    public bool splitable { get; set; }
    public bool waitingForBet { get; set; }
    public bool waitingForPull { get; set; }
    public bool notPlaying { get; set; }
    public bool doubled { get; set; }
}

/// <summary>
///     The dealer as a client sees them.
/// </summary>
/// <remarks>
///     Exposes only the face-up total. The domain type derives a full <c>points</c> from every card and hid
///     it behind a <c>ShouldSerializepoints</c> hook; omitting the property outright says the same thing
///     without depending on a serializer callback to enforce it.
/// </remarks>
public class BlackjackDealerDto
{
    public BlackjackDealerDto()
    {
    }

    public BlackjackDealerDto(BlackjackDealer dealer)
    {
        name = dealer.name;
        cards = dealer.cards.Select(card => new PlayingCardDto(card)).ToList();
        pointsDTO = dealer.pointsDTO;
        money = dealer.money;
    }

    public string? name { get; set; }
    public List<PlayingCardDto> cards { get; set; } = new();

    /// <summary>Total of the face-up cards only.</summary>
    public int? pointsDTO { get; set; }

    // No `blackjack` flag: it is derived from every card including the face-down one, so sending it would
    // announce a dealer 21 before the reveal. Nothing in the client reads it.

    public double money { get; set; }
}
