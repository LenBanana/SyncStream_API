using System;
using System.Collections.Generic;
using SyncStreamAPI.Wire.Users;

namespace SyncStreamAPI.Models.RTMP;

public class LiveUser
{
    public string id { get; set; }
    public string userName { get; set; }
    public List<UserSummaryDto> watchMember { get; set; } = new();
    public DateTime created { get; set; } = DateTime.UtcNow;

}