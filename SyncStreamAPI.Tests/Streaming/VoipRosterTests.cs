using System.Linq;
using SyncStreamAPI.ServerData;
using Xunit;

namespace SyncStreamAPI.Tests.Streaming;

/// <summary>Who is in which call, which is what decides who may send into a call's SFU room.</summary>
public class VoipRosterTests
{
    [Fact]
    public void Joining_returns_everyone_already_in_that_call_and_nobody_else()
    {
        var roster = new VoipRoster();
        roster.Join("a", "room-1", "alice");
        roster.Join("b", "room-2", "bob");

        var (previous, others) = roster.Join("c", "room-1", "carol");

        Assert.Null(previous);
        var other = Assert.Single(others);
        Assert.Equal("a", other.ParticipantId);
        Assert.Equal("alice", other.ParticipantName);
    }

    [Fact]
    public void A_connection_is_in_one_call_at_a_time()
    {
        var roster = new VoipRoster();
        roster.Join("a", "room-1", "alice");

        var (previous, _) = roster.Join("a", "room-2", "alice");

        Assert.Equal("room-1", previous);
        Assert.False(roster.IsIn("a", "room-1"));
        Assert.True(roster.IsIn("a", "room-2"));
    }

    [Fact]
    public void Leaving_reports_the_call_left()
    {
        var roster = new VoipRoster();
        roster.Join("a", "room-1", "alice");

        Assert.Equal("room-1", roster.Leave("a"));
        Assert.Null(roster.Leave("a"));
        Assert.Null(roster.RoomOf("a"));
    }

    [Fact]
    public void A_new_participant_starts_unmuted_with_camera_and_screen_off()
    {
        var roster = new VoipRoster();
        roster.Join("a", "room-1", "alice");

        var participant = roster.Describe("a")!;

        Assert.False(participant.IsMuted);
        Assert.True(participant.IsVideoMuted);
        Assert.True(participant.IsScreenMuted);
    }

    [Fact]
    public void Status_is_kept_so_later_joiners_see_it()
    {
        var roster = new VoipRoster();
        roster.Join("a", "room-1", "alice");
        roster.UpdateStatus("a", isMuted: true, isVideoMuted: false, isScreenMuted: true);

        var (_, others) = roster.Join("b", "room-1", "bob");

        var alice = others.Single();
        Assert.True(alice.IsMuted);
        Assert.False(alice.IsVideoMuted);
    }

    [Fact]
    public void Status_from_someone_not_in_a_call_is_ignored()
    {
        Assert.Null(new VoipRoster().UpdateStatus("nobody", true, true, true));
    }
}
