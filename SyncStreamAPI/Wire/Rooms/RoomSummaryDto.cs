using System.Collections.Generic;
using System.Linq;
using SyncStreamAPI.Models;

namespace SyncStreamAPI.Wire.Rooms;

/// <summary>
///     What the lobby needs to render a room card, and nothing more. This type is broadcast to every
///     connected client on every membership change, so anything added here is public by definition.
/// </summary>
/// <remarks>
///     Deliberately absent, all of which the previous full-<see cref="Room" /> broadcast exposed:
///     the room password (which the join check compared against verbatim, so broadcasting it defeated
///     the password entirely), every member's connection id, every member's private message log,
///     the buffered chat history, and the banned-member list.
/// </remarks>
public class RoomSummaryDto
{
    public RoomSummaryDto()
    {
    }

    public RoomSummaryDto(Room room)
    {
        var server = room.server;

        uniqueId = room.uniqueId;
        name = room.name;
        isPrivileged = room.isPrivileged;
        deletable = room.deletable;
        hasPassword = !string.IsNullOrEmpty(room.password);

        isplaying = server.isplaying;
        title = server.title;
        currenttime = server.currenttime;

        members = server.members
            .Where(member => member != null)
            .Select(member => new RoomMemberSummaryDto(member))
            .ToList();

        playlist = server.playlist
            .Where(video => video != null)
            .Select(video => new RoomPlaylistEntryDto(video))
            .ToList();
    }

    public string? uniqueId { get; set; }
    public string? name { get; set; }
    public bool isPrivileged { get; set; }
    public bool deletable { get; set; }

    /// <summary>Whether a password is required, never the password itself.</summary>
    public bool hasPassword { get; set; }

    public bool isplaying { get; set; }
    public string? title { get; set; }
    public double currenttime { get; set; }

    public List<RoomMemberSummaryDto> members { get; set; } = new();
    public List<RoomPlaylistEntryDto> playlist { get; set; } = new();
}

/// <summary>A room member as seen from the lobby: a display name and whether they host.</summary>
public class RoomMemberSummaryDto
{
    public RoomMemberSummaryDto()
    {
    }

    public RoomMemberSummaryDto(Member member)
    {
        username = member.username;
        ishost = member.ishost;
    }

    public string? username { get; set; }
    public bool ishost { get; set; }
}

/// <summary>A playlist entry as seen from the lobby. The url is included because the lobby derives a source icon from it.</summary>
public class RoomPlaylistEntryDto
{
    public RoomPlaylistEntryDto()
    {
    }

    public RoomPlaylistEntryDto(DreckVideo video)
    {
        title = video.title;
        url = video.url;
    }

    public string? title { get; set; }
    public string? url { get; set; }
}
