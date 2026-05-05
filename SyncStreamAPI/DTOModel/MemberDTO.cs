namespace SyncStreamAPI.DTOModel;

public class MemberDTO
{
    public MemberDTO(string Username, bool IsHost, bool IsStreaming = false)
    {
        username = Username;
        ishost = IsHost;
        isstreaming = IsStreaming;
    }

    public string username { get; set; }
    public bool ishost { get; set; }
    /// <summary>True while the member is sharing their screen (P2P WebRTC).</summary>
    public bool isstreaming { get; set; }
}