using System.Linq;
using Microsoft.AspNetCore.WebUtilities;
using SyncStreamAPI.Helper.Streaming;
using Xunit;

namespace SyncStreamAPI.Tests.Streaming;

/// <summary>
///     Guests may watch the live video their room shows, which is decided by reading the stream out of the
///     playlist entry; and players are sent to the RTMP application the API itself publishes to.
/// </summary>
public class LiveStreamUrlTests
{
    [Theory]
    [InlineData("https://live.drecktu.be/live?stream=alice", "alice")]
    [InlineData("https://live.drecktu.be/live?stream=alice&app=live&port=1935", "alice")]
    [InlineData("http://drecktu.be:8088/live?port=1935&app=live&stream=Bob", "Bob")]
    [InlineData("rtmp://drecktu.be/live/carol", "carol")]
    public void The_stream_is_read_out_of_a_live_video(string url, string stream)
    {
        Assert.Equal(stream, LiveStreamUrls.StreamNameOf(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("https://live.drecktu.be/live")]
    public void Anything_else_names_no_stream(string? url)
    {
        Assert.Null(LiveStreamUrls.StreamNameOf(url));
    }

    [Fact]
    public void A_play_url_points_at_the_rtmp_application_and_carries_the_ticket()
    {
        var url = LiveStreamUrls.PlayUrl("https://live.example/live", "rtmp://rtmp-server:1936/stage", "share-1", "t.k");
        var query = QueryHelpers.ParseQuery(url[url.IndexOf('?')..]);

        Assert.StartsWith("https://live.example/live?", url);
        Assert.Equal("1936", query["port"].Single());
        Assert.Equal("stage", query["app"].Single());
        Assert.Equal("share-1", query["stream"].Single());
        Assert.Equal("t.k", query["token"].Single());
    }

    [Fact]
    public void An_rtmp_url_without_a_port_means_the_standard_one()
    {
        var url = LiveStreamUrls.PlayUrl("https://live.example/live", "rtmp://rtmp-server/live", "alice", "t");
        Assert.Equal("1935", QueryHelpers.ParseQuery(url[url.IndexOf('?')..])["port"].Single());
    }
}
