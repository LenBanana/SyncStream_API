using System.Collections.Generic;

namespace SyncStreamAPI.Models.GameModels.Members
{
    public class DurakMember
    {
        public bool ShouldSerializeConnectionId() { return false; }
        public string ConnectionId { get; set; }
        public string username { get; set; }
        public List<PlayingCard> cards { get; set; } = new List<PlayingCard>();
        public bool attacking { get; set; } = false;
        public bool defending { get; set; } = false;
    }
}
