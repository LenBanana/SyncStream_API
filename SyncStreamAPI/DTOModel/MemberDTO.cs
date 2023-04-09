namespace SyncStreamAPI.DTOModel
{
    public class MemberDTO
    {
        public string username { get; set; }
        public bool ishost { get; set; }

        public MemberDTO(string Username, bool IsHost)
        {
            username = Username;
            ishost = IsHost;
        }

    }
}
