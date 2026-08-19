using System;
using System.Collections.Generic;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.Wire.Users;

namespace SyncStreamAPI.Wire.Live;

/// <summary>
///     A live stream as a client sees it.
/// </summary>
/// <remarks>
///     The domain <see cref="LiveUser" /> keys itself by the owner's RTMP stream token. The type this
///     replaces inherited from it and blanked that field in its constructor, which is the same pattern that
///     made the user DTO ship stream tokens and API keys: it works until someone adds a field, or forgets a
///     line. Omitting the property is not something anyone can forget.
/// </remarks>
public class LiveUserDto
{
    public LiveUserDto()
    {
    }

    public LiveUserDto(LiveUser user)
    {
        userName = user.userName;
        watchMember = user.watchMember;
        created = user.created;
    }

    public string? userName { get; set; }
    public List<UserSummaryDto> watchMember { get; set; } = new();
    public DateTime created { get; set; }
}
