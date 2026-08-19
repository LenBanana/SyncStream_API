using System;
using System.Collections.Generic;
using System.Linq;

using SyncStreamAPI.Models.Collections;

namespace SyncStreamAPI.Models;

public class Server
{
    public Server()
    {
        playlist = new List<DreckVideo>();
        members = new MemberList();
        chatmessages = new List<ChatMessage>();
        currentVideo = new DreckVideo { title = "Nothing playing", url = "", ended = true };
        isplaying = false;
        currenttime = 0;
    }

    public double _currenttime { get; set; }

    public double currenttime
    {
        get => playlist.Count > 0 ? _currenttime : 0;
        set => _currenttime = value;
    }

    public bool isplaying { get; set; }

    /// <summary>
    ///     When <see cref="currenttime" /> was last reported by the host.
    /// </summary>
    /// <remarks>
    ///     Lets a position be dated when relayed, so a viewer can tell a fresh report from one that has been
    ///     sitting here, and so a late joiner is told where playback actually is rather than where it was when
    ///     the host last happened to send an update.
    /// </remarks>
    public DateTime LastTimeUpdateUtc { get; set; } = DateTime.UtcNow;
    public string title => playlist.Count > 0 ? playlist[0].title : "Nothing playing";
    private DreckVideo _currentVideo { get; set; }

    public DreckVideo currentVideo
    {
        get => _currentVideo;
        set
        {
            _currentVideo = value;

            // A new video starts at the beginning, as of now. Resetting the position without re-dating it left
            // the previous video's timestamp attached to a position of zero, and a viewer who asked for the room
            // state in that window added the gap since that timestamp to zero and seeked that far into a video
            // that had only just started. Every path that changes the video assigns this property, so dating it
            // here covers skipping, playing a specific entry, and a new video arriving into an ended room.
            currenttime = 0;
            LastTimeUpdateUtc = DateTime.UtcNow;
        }
    }

    public List<DreckVideo> playlist { get; set; } = new();
    public MemberList members { get; set; } = new();
    public List<ChatMessage> chatmessages { get; set; } = new();

    /// <summary>
    ///     Identities barred from this room.
    /// </summary>
    /// <remarks>
    ///     Keyed by identity rather than by connection id, which is what made a ban last exactly as long as
    ///     the banned person's current connection: a page refresh produced a new connection id and let them
    ///     straight back in.
    /// </remarks>
    public HashSet<string> BannedIdentities { get; } = new();

    /// <summary>
    ///     The room this state belongs to.
    /// </summary>
    /// <remarks>
    ///     Set when the room is created. It used to be derived from the first member's RoomId, so an empty
    ///     room reported the empty string, and chat sent to that room went to a group named "" which nobody
    ///     is in.
    /// </remarks>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>The member on this connection, or null.</summary>
    public Member? FindByConnection(string? connectionId)
    {
        return connectionId == null ? null : members.Find(member => member?.ConnectionId == connectionId);
    }

    /// <summary>The member with this identity, or null. Used so a reconnecting account reclaims its seat.</summary>
    public Member? FindByIdentity(string? identity)
    {
        return string.IsNullOrEmpty(identity)
            ? null
            : members.Find(member => member?.Identity == identity);
    }

    public bool IsBanned(string identity)
    {
        return !string.IsNullOrEmpty(identity) && BannedIdentities.Contains(identity);
    }

    /// <summary>Adds a member, making them host when the room was empty.</summary>
    public void Join(Member member)
    {
        member.ishost = members.Count == 0;
        members.Add(member);
    }

    /// <summary>
    ///     Removes a member and hands the host role on when they held it.
    /// </summary>
    /// <returns>The member promoted to host, or null when nobody was or nobody is left.</returns>
    /// <remarks>
    ///     Succession is by join time rather than by list position. The previous code promoted
    ///     <c>members[0]</c> from six different places, which depends on an ordering nothing guaranteed once
    ///     joins and leaves interleaved.
    /// </remarks>
    public Member? Leave(Member member)
    {
        var wasHost = member.ishost;
        if (!members.Remove(member)) return null;

        return wasHost ? PromoteLongestPresent() : null;
    }

    /// <summary>Gives the host role to the member who has been here longest.</summary>
    public Member? PromoteLongestPresent()
    {
        var snapshot = members.Snapshot;
        if (snapshot.Count == 0) return null;

        var successor = snapshot
            .Where(candidate => candidate != null)
            .OrderBy(candidate => candidate.JoinedUtc)
            .FirstOrDefault();
        if (successor == null) return null;

        foreach (var candidate in snapshot)
            if (candidate != null)
                candidate.ishost = ReferenceEquals(candidate, successor);

        return successor;
    }
}