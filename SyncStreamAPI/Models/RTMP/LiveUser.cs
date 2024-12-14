using System;
using System.Collections.Generic;

namespace SyncStreamAPI.Models.RTMP;

public class LiveUser
{
    public string id { get; set; }
    public string userName { get; set; }
    public List<UserDTO> watchMember { get; set; } = new();
    public DateTime created { get; set; } = DateTime.UtcNow;

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
        userName = user.userName;
        watchMember = user.watchMember;
        created = user.created;
    }
}