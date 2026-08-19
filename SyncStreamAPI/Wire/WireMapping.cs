using System;
using SyncStreamAPI.Wire.Playback;
using System.Collections.Generic;
using System.Linq;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Wire.Rooms;
using SyncStreamAPI.Wire.Users;
using SyncStreamAPI.Models.GameModels.Blackjack;
using SyncStreamAPI.Models.GameModels.Members;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.Wire.Live;
using SyncStreamAPI.Wire.Games;

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

    public static BlackjackMemberDto ToDto(this BlackjackMember member)
    {
        return new BlackjackMemberDto(member);
    }

    public static List<BlackjackMemberDto> ToDtos(this IEnumerable<BlackjackMember>? members)
    {
        return members?.Select(member => member.ToDto()).ToList() ?? new List<BlackjackMemberDto>();
    }

    public static BlackjackDealerDto ToDto(this BlackjackDealer dealer)
    {
        return new BlackjackDealerDto(dealer);
    }

    public static List<GallowMemberDto> ToDtos(this IEnumerable<GallowMember>? members)
    {
        return members?.Select(member => new GallowMemberDto(member)).ToList() ?? new List<GallowMemberDto>();
    }

    public static List<LiveUserDto> ToDtos(this IEnumerable<LiveUser>? users)
    {
        return users?.Select(user => new LiveUserDto(user)).ToList() ?? new List<LiveUserDto>();
    }

    /// <summary>
    ///     Dates the room's current position for relay to viewers.
    /// </summary>
    /// <remarks>
    ///     While playing, the age is how long ago the host reported the position, so a viewer can add it back.
    ///     While paused it is zero, because a paused position does not advance and extrapolating it would send
    ///     viewers past where everyone else is sitting.
    /// </remarks>
    public static TimeSyncDto ToTimeSync(this Server server)
    {
        var age = server.isplaying
            ? Math.Max(0, (DateTime.UtcNow - server.LastTimeUpdateUtc).TotalMilliseconds)
            : 0;

        return new TimeSyncDto(server.currenttime, age, server.isplaying);
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
