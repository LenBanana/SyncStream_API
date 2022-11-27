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
        public LiveUserDTO ToDTO()
        {
            return new LiveUserDTO(this);
        }
    }

    public class LiveUserDTO : LiveUser
    {
        public LiveUserDTO(LiveUser user)
        {
            id = "";
            this.userName = user.userName;
            this.watchMember = user.watchMember;
            this.created = user.created;
        }
    }
}
