using SyncStreamAPI.Models.WebRTC;
using Xunit;

namespace SyncStreamAPI.Tests.Streaming;

/// <summary>
///     Every SFU room is checked against the chat room it belongs to, so the room has to be read back out of the
///     id exactly, whatever the chat room is called.
/// </summary>
public class SfuRoomIdTests
{
    [Fact]
    public void A_stage_id_names_its_room()
    {
        Assert.True(SfuRoomId.TryParse(SfuRoomId.Stage("abc"), out var id));
        Assert.Equal(new SfuRoomId(SfuRoomKind.Stage, "abc", null), id);
    }

    [Fact]
    public void A_voice_id_names_its_room()
    {
        Assert.True(SfuRoomId.TryParse(SfuRoomId.Voice("abc"), out var id));
        Assert.Equal(new SfuRoomId(SfuRoomKind.Voice, "abc", null), id);
    }

    [Fact]
    public void A_live_id_names_its_room_and_streamer()
    {
        Assert.True(SfuRoomId.TryParse(SfuRoomId.Live("abc", "conn-1"), out var id));
        Assert.Equal(new SfuRoomId(SfuRoomKind.Live, "abc", "conn-1"), id);
    }

    [Fact]
    public void A_room_id_containing_a_colon_survives_a_live_id()
    {
        // Rooms made before ids were restricted can hold any character; the streamer, a connection id, never
        // contains a colon, so the split is taken from the right.
        Assert.True(SfuRoomId.TryParse(SfuRoomId.Live("old:room", "conn-1"), out var id));
        Assert.Equal("old:room", id.RoomId);
        Assert.Equal("conn-1", id.StreamerId);
    }

    [Fact]
    public void Parsing_round_trips()
    {
        foreach (var value in new[] { SfuRoomId.Stage("r"), SfuRoomId.Voice("r"), SfuRoomId.Live("r", "c") })
        {
            Assert.True(SfuRoomId.TryParse(value, out var id));
            Assert.Equal(value, id.Value);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("stage:")]
    [InlineData("voice:")]
    [InlineData("live:abc")]
    [InlineData("live:abc:")]
    [InlineData("live::conn")]
    [InlineData("live-watch:abc:conn")]
    public void Anything_else_is_not_an_sfu_room(string? value)
    {
        // The old bare room id and the old live-watch prefix included: an id that does not say which kind of
        // room it is cannot be checked against the right rule.
        Assert.False(SfuRoomId.TryParse(value, out _));
    }
}
