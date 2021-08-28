using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
