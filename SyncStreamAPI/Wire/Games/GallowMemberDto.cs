using SyncStreamAPI.Models.GameModels.Members;

namespace SyncStreamAPI.Wire.Games;

/// <summary>
///     A gallows player as a client sees them.
/// </summary>
/// <remarks>
///     The domain <see cref="GallowMember" /> carries a connection id and subscribes to the room member's
///     kick event. Unlike its blackjack counterpart it had no serializer hook hiding the connection id, so
///     <c>gallowusers</c> was broadcasting every player's connection id to the whole room.
/// </remarks>
public class GallowMemberDto
{
    public GallowMemberDto()
    {
    }

    public GallowMemberDto(GallowMember member)
    {
        username = member.username;
        isDrawing = member.isDrawing;
        gallowPoints = member.gallowPoints;
        guessedGallow = member.guessedGallow;
        guessedGallowTime = member.guessedGallowTime;
    }

    public string? username { get; set; }
    public bool isDrawing { get; set; }
    public double gallowPoints { get; set; }
    public bool guessedGallow { get; set; }
    public int guessedGallowTime { get; set; }
}
