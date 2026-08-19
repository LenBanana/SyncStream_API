using SyncStreamAPI.Models.GameModels.Chess;
using SyncStreamAPI.Wire.Rooms;

namespace SyncStreamAPI.Wire.Games;

/// <summary>
///     A chess game as sent to a room.
/// </summary>
/// <remarks>
///     The domain <see cref="ChessGame" /> holds full <see cref="Models.Member" /> objects because the
///     disconnect handling matches players by connection id. Sending it directly also sent both players'
///     connection ids and their private message logs to everyone in the room, so the players are reduced
///     to a display name here. No client reads anything else off them.
/// </remarks>
public class ChessGameDto
{
    public ChessGameDto()
    {
    }

    public ChessGameDto(ChessGame game)
    {
        LightPlayer = game.LightPlayer == null ? null : new RoomMemberSummaryDto(game.LightPlayer);
        DarkPlayer = game.DarkPlayer == null ? null : new RoomMemberSummaryDto(game.DarkPlayer);
        LightPlayerAi = game.LightPlayerAi;
        DarkPlayerAi = game.DarkPlayerAi;
        UniqueId = game.UniqueId;
        GameFEN = game.GameFEN;
    }

    public RoomMemberSummaryDto? LightPlayer { get; set; }
    public RoomMemberSummaryDto? DarkPlayer { get; set; }
    public bool LightPlayerAi { get; set; }
    public bool DarkPlayerAi { get; set; }
    public string? UniqueId { get; set; }
    public string? GameFEN { get; set; }
}
