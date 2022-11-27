using System;
using System.Collections.Generic;

namespace SyncStreamAPI.Models.RTMP
{
    public class LiveUser
    {
        public string id { get; set; }
        public string userName { get; set; }
        public List<UserDTO> watchMember { get; set; } = new List<UserDTO>();
        public DateTime created { get; set; } = DateTime.Now;
    }
}
