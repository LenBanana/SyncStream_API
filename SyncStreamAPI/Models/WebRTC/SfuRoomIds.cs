namespace SyncStreamAPI.Models.WebRTC;

/// <summary>What an SFU room carries, which decides who may join it and who may send into it.</summary>
public enum SfuRoomKind
{
    /// <summary>The room's main player: a room-stage broadcast or a file share.</summary>
    Stage,

    /// <summary>One member's opt-in live stream, watched in the popout by whoever chooses to.</summary>
    Live,

    /// <summary>The room's voice and video call.</summary>
    Voice
}

/// <summary>
///     An SFU room id, parsed. Every SFU room belongs to exactly one chat room, which is what membership checks
///     are made against.
/// </summary>
/// <param name="StreamerId">The connection that owns a <see cref="SfuRoomKind.Live" /> room; null otherwise.</param>
public sealed record SfuRoomId(SfuRoomKind Kind, string RoomId, string? StreamerId)
{
    private const string StagePrefix = "stage:";
    private const string LivePrefix = "live:";
    private const string VoicePrefix = "voice:";

    public static string Stage(string roomId) => StagePrefix + roomId;

    public static string Live(string roomId, string streamerId) => $"{LivePrefix}{roomId}:{streamerId}";

    public static string Voice(string roomId) => VoicePrefix + roomId;

    public string Value => Kind switch
    {
        SfuRoomKind.Stage => Stage(RoomId),
        SfuRoomKind.Live => Live(RoomId, StreamerId!),
        _ => Voice(RoomId)
    };

    /// <remarks>
    ///     A live room id ends in a connection id, which never contains a colon, so the room id is whatever lies
    ///     between the prefix and the last colon. That keeps a room id with a colon in it parseable.
    /// </remarks>
    public static bool TryParse(string? value, out SfuRoomId id)
    {
        id = null!;
        if (string.IsNullOrEmpty(value)) return false;

        if (value.StartsWith(StagePrefix) && value.Length > StagePrefix.Length)
        {
            id = new SfuRoomId(SfuRoomKind.Stage, value[StagePrefix.Length..], null);
            return true;
        }

        if (value.StartsWith(VoicePrefix) && value.Length > VoicePrefix.Length)
        {
            id = new SfuRoomId(SfuRoomKind.Voice, value[VoicePrefix.Length..], null);
            return true;
        }

        if (value.StartsWith(LivePrefix))
        {
            var rest = value[LivePrefix.Length..];
            var separator = rest.LastIndexOf(':');
            if (separator <= 0 || separator == rest.Length - 1) return false;
            id = new SfuRoomId(SfuRoomKind.Live, rest[..separator], rest[(separator + 1)..]);
            return true;
        }

        return false;
    }
}
