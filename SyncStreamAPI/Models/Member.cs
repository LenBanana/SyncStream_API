using System;
using System.Collections.Generic;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Models.GameModels.Members;

namespace SyncStreamAPI.Models;

/// <summary>
///     Somebody present in a room.
/// </summary>
/// <remarks>
///     Liveness used to be a per-member <see cref="System.Timers.Timer" /> with a ten second fuse, kept alive
///     by the client calling UpdateUser every 2.5 seconds. That duplicated work SignalR already does: its
///     keep-alive and client timeout drop a dead connection and raise OnDisconnectedAsync, which is where
///     departures are handled now. One OS timer per member and a poll per member per 2.5 seconds bought
///     nothing, and the poll's fall-back path silently locked reconnecting users out of password rooms.
/// </remarks>
public class Member
{
    public delegate void KickEvent(Member e);

    /// <summary>
    ///     Who this member is, stable across reconnects for an account and not for a guest.
    /// </summary>
    /// <remarks>
    ///     Comes from the connection's identity, never from the client. An account reconnecting reclaims its
    ///     seat because the identity matches; a guest gets a new one, which is correct, since a guest has no
    ///     durable identity to reclaim with.
    /// </remarks>
    public string Identity { get; set; } = string.Empty;

    public string? username { get; set; }

    public string? RoomId { get; set; }

    public string? ConnectionId { get; set; }

    /// <summary>When this member joined, for display and for deterministic host succession.</summary>
    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;

    public bool ishost { get; set; }

    /// <summary>True while the member is actively streaming (P2P screen-share).</summary>
    public bool IsStreaming { get; set; }

    public Dictionary<string, List<string>> PrivateMessages { get; set; } = new();

    /// <summary>
    ///     Raised when the member leaves, so the games can drop them from a hand or a round.
    /// </summary>
    public event KickEvent? Kicked;

    public MemberDTO ToDTO()
    {
        return new MemberDTO(username, ishost, IsStreaming, ConnectionId);
    }

    public GallowMember ToGallowMember()
    {
        return new GallowMember(this);
    }

    public BlackjackMember ToBlackjackMember(BlackjackManager manager)
    {
        return new BlackjackMember(this, manager);
    }

    /// <summary>Announces that this member has left. Raised by whoever removes them from a room.</summary>
    public void InvokeKick()
    {
        Kicked?.Invoke(this);
    }

    public List<string> GetMessages(string User)
    {
        return PrivateMessages[User];
    }

    public string AddMessage(string User, string Message)
    {
        if (!PrivateMessages.ContainsKey(User)) PrivateMessages.Add(User, new List<string>());

        var FullMessage = string.Format("{0} {1}: {2}", DateTime.UtcNow.ToString("HH:mm"), username, Message);
        PrivateMessages[User].Add(FullMessage);
        if (PrivateMessages[User].Count > 150) PrivateMessages[User].RemoveAt(0);

        return FullMessage;
    }

    public void RemoveMessages(string User)
    {
        if (!PrivateMessages.ContainsKey(User)) PrivateMessages.Remove(User);
    }
}
