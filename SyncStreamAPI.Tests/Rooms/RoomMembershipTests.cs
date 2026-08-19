using System;
using System.Linq;
using SyncStreamAPI.Models;
using Xunit;

namespace SyncStreamAPI.Tests.Rooms;

/// <summary>
///     Joining, leaving, host succession and bans.
/// </summary>
public class RoomMembershipTests
{
    private static Member NewMember(string identity, string username, DateTime? joined = null)
    {
        return new Member
        {
            Identity = identity,
            username = username,
            ConnectionId = $"conn-{username}",
            JoinedUtc = joined ?? DateTime.UtcNow
        };
    }

    [Fact]
    public void The_first_member_becomes_host_and_later_ones_do_not()
    {
        var server = new Server();

        var first = NewMember("user:1", "first");
        var second = NewMember("user:2", "second");
        server.Join(first);
        server.Join(second);

        Assert.True(first.ishost);
        Assert.False(second.ishost);
    }

    [Fact]
    public void The_host_role_passes_to_whoever_has_been_here_longest()
    {
        // Succession used to promote members[0] from six separate places, which depends on an ordering that
        // nothing guaranteed once joins and leaves interleaved. It is decided by join time now.
        var server = new Server();
        var host = NewMember("user:1", "host", DateTime.UtcNow.AddMinutes(-10));
        var newest = NewMember("user:2", "newest", DateTime.UtcNow);
        var longest = NewMember("user:3", "longest", DateTime.UtcNow.AddMinutes(-5));

        server.Join(host);
        server.Join(newest);
        server.Join(longest);

        var promoted = server.Leave(host);

        Assert.Same(longest, promoted);
        Assert.True(longest.ishost);
        Assert.False(newest.ishost);
        Assert.Single(server.members.Where(member => member.ishost));
    }

    [Fact]
    public void A_non_host_leaving_changes_nothing()
    {
        var server = new Server();
        var host = NewMember("user:1", "host");
        var other = NewMember("user:2", "other");
        server.Join(host);
        server.Join(other);

        var promoted = server.Leave(other);

        Assert.Null(promoted);
        Assert.True(host.ishost);
        Assert.Single(server.members);
    }

    [Fact]
    public void The_last_member_leaving_promotes_nobody()
    {
        var server = new Server();
        var only = NewMember("user:1", "only");
        server.Join(only);

        Assert.Null(server.Leave(only));
        Assert.Empty(server.members);
    }

    [Fact]
    public void Leaving_twice_is_harmless()
    {
        var server = new Server();
        var host = NewMember("user:1", "host");
        var other = NewMember("user:2", "other");
        server.Join(host);
        server.Join(other);

        server.Leave(host);

        // A disconnect can race an explicit leave, so the second removal has to be a no-op rather than
        // promoting a second host.
        Assert.Null(server.Leave(host));
        Assert.True(other.ishost);
        Assert.Single(server.members);
    }

    [Fact]
    public void A_ban_follows_the_identity_not_the_connection()
    {
        // Recording a ban against a connection id made it last exactly as long as that connection: a page
        // refresh produced a new id and let the banned person back in.
        var server = new Server();
        var member = NewMember("user:7", "banned");
        server.Join(member);

        server.BannedIdentities.Add(member.Identity);
        server.Leave(member);

        Assert.True(server.IsBanned("user:7"));
        Assert.False(server.IsBanned("user:8"));

        // Same account, new connection: still barred.
        Assert.True(server.IsBanned(NewMember("user:7", "banned-again").Identity));
    }

    [Fact]
    public void An_account_can_be_found_by_identity_so_a_reconnect_reclaims_its_seat()
    {
        var server = new Server();
        var member = NewMember("user:7", "someone");
        server.Join(member);

        var found = server.FindByIdentity("user:7");
        Assert.Same(member, found);

        // Reclaiming keeps the host role and the place in the turn order rather than appending a duplicate.
        found!.ConnectionId = "conn-reconnected";

        Assert.Single(server.members);
        Assert.True(server.members[0]!.ishost);
        Assert.Same(member, server.FindByConnection("conn-reconnected"));
        Assert.Null(server.FindByConnection("conn-someone"));
    }

    [Fact]
    public void A_rooms_state_knows_its_own_room_even_when_empty()
    {
        // RoomId was derived from the first member, so an empty room reported the empty string and chat sent
        // to it went to a SignalR group named "" that nobody is in.
        var room = new Room("Movie night", "movie-night", true, false);

        Assert.Empty(room.server.members);
        Assert.Equal("movie-night", room.server.RoomId);
    }
}
