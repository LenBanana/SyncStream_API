using System;
using System.Collections.Generic;
using SyncStreamAPI.Wire.Users;

namespace SyncStreamAPI.Models.RTMP;

/// <summary>Someone streaming from OBS. Lock <see cref="watchMember" /> to read or change it.</summary>
public class LiveUser
{
    /// <summary>The owner's stream token, which keys the live list.</summary>
    public required string id { get; set; }
    public required string userName { get; set; }

    /// <summary>
    ///     The stream name as OBS published it. The streaming server matches stream names exactly, while the
    ///     username they are checked against is matched ignoring case.
    /// </summary>
    public required string StreamName { get; set; }

    public List<UserSummaryDto> watchMember { get; set; } = new();
    public DateTime created { get; set; } = DateTime.UtcNow;
}
