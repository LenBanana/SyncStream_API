namespace SyncStreamAPI.DTOModel;

public class MemberDTO
{
    public MemberDTO(string Username, bool IsHost, bool IsStreaming = false, string? ConnectionId = null)
    {
        username = Username;
        ishost = IsHost;
        isstreaming = IsStreaming;
        connectionId = ConnectionId ?? string.Empty;
    }

    public string username { get; set; }
    public bool ishost { get; set; }
    /// <summary>True while the member is sharing their screen (P2P WebRTC).</summary>
    public bool isstreaming { get; set; }
    /// <summary>SignalR connectionId — used by the frontend to target a specific streamer.</summary>
    public string connectionId { get; set; } = string.Empty;
}