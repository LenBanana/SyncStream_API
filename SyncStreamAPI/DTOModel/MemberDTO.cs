namespace SyncStreamAPI.DTOModel;

public class MemberDTO
{
    public MemberDTO(string Username, bool IsHost)
    {
        username = Username;
        ishost = IsHost;
    }

    public string username { get; set; }
    public bool ishost { get; set; }
}