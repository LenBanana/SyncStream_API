using System.Collections.Generic;
using System.Linq;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Wire.Rooms;
using SyncStreamAPI.Wire.Users;

namespace SyncStreamAPI.Wire;

/// <summary>
///     The single place domain and persistence types are converted into wire types. Hub and controller
///     code should map here rather than handing a <see cref="DbUser" /> or a <see cref="Room" /> to a
///     client callback.
/// </summary>
public static class WireMapping
{
    public static UserSummaryDto ToSummary(this DbUser user)
    {
        return new UserSummaryDto(user);
    }

    public static List<UserSummaryDto> ToSummaries(this IEnumerable<DbUser>? users)
    {
        return users?.Select(user => user.ToSummary()).ToList() ?? new List<UserSummaryDto>();
    }

    /// <summary>Only for sends addressed to the user being described.</summary>
    public static SelfProfileDto ToSelfProfile(this DbUser user)
    {
        return new SelfProfileDto(user);
    }

    public static RoomSummaryDto ToSummary(this Room room)
    {
        return new RoomSummaryDto(room);
    }

    public static List<RoomSummaryDto> ToSummaries(this IEnumerable<Room>? rooms)
    {
        return rooms?.Where(room => room != null).Select(room => room.ToSummary()).ToList()
               ?? new List<RoomSummaryDto>();
    }
}
