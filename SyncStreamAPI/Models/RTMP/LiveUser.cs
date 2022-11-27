using System;
using System.Collections.Generic;

namespace SyncStreamAPI.Models.RTMP
{
    public class LiveUser
    {
        public string id { get; set; }
        public string userName { get; set; }
        public List<UserDTO> watchMember { get; set; } 
        public DateTime created { get; set; } = new DateTime();
    }
}
