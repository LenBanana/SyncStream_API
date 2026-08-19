namespace SyncStreamAPI.Wire.Rooms;

/// <summary>
///     Inbound room creation. Only the fields a creator gets to choose.
/// </summary>
/// <remarks>
///     Binding the domain <see cref="Models.Room" /> here also bound its <c>server</c> property, which
///     let a caller seed a brand new room with a fabricated member list, chat history, playlist and
///     banned-member list. The live state is built by the server instead.
/// </remarks>
public class CreateRoomRequestDto
{
    public string? uniqueId { get; set; }
    public string? name { get; set; }

    /// <summary>SHA512 of the room password, or null for an open room.</summary>
    public string? password { get; set; }

    public bool deletable { get; set; } = true;
}
