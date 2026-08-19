using SyncStreamAPI.Models;
using Xunit;

namespace SyncStreamAPI.Tests.Rooms;

/// <summary>
///     Naming a Twitch link in a playlist.
/// </summary>
/// <remarks>
///     The block this replaces contained <c>key.url = key.url;</c> where it meant to store the id it had just
///     extracted, so the extraction was thrown away and the title interpolated the whole URL instead of the
///     recording it named. Entries read "Twitch Video - https://www.twitch.tv/videos/123".
/// </remarks>
public class TwitchSourceTests
{
    [Theory]
    [InlineData("https://www.twitch.tv/somestreamer", TwitchSourceKind.Channel, "somestreamer")]
    [InlineData("twitch.tv/somestreamer", TwitchSourceKind.Channel, "somestreamer")]
    [InlineData("https://m.twitch.tv/somestreamer", TwitchSourceKind.Channel, "somestreamer")]
    [InlineData("https://www.twitch.tv/videos/123456789", TwitchSourceKind.Video, "123456789")]
    [InlineData("https://www.twitch.tv/videos/123456789?t=1h2m3s", TwitchSourceKind.Video, "123456789")]
    [InlineData("https://clips.twitch.tv/TastySlugName", TwitchSourceKind.Clip, "TastySlugName")]
    [InlineData("https://www.twitch.tv/somestreamer/clip/TastySlugName-a_1", TwitchSourceKind.Clip,
        "TastySlugName-a_1")]
    [InlineData("https://clips.twitch.tv/embed?clip=TastySlugName&parent=example.com", TwitchSourceKind.Clip,
        "TastySlugName")]
    public void Reads_what_a_link_points_at(string url, TwitchSourceKind kind, string id)
    {
        var source = TwitchSource.FromUrl(url);

        Assert.NotNull(source);
        Assert.Equal(kind, source!.Kind);
        Assert.Equal(id, source.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("https://nottwitch.tv/somestreamer")]
    // A host that merely ends with the real one, which the contains-check this replaces would have accepted.
    [InlineData("https://evil-twitch.tv/somestreamer")]
    public void Refuses_anything_it_cannot_name(string? url)
    {
        Assert.Null(TwitchSource.FromUrl(url));
    }

    [Fact]
    public void Names_a_recording_by_its_id_rather_than_its_url()
    {
        var source = TwitchSource.FromUrl("https://www.twitch.tv/videos/123456789");

        Assert.Equal("Twitch Video - 123456789", source!.DisplayTitle());
    }

    [Fact]
    public void Names_a_clip_by_its_slug()
    {
        var source = TwitchSource.FromUrl("https://clips.twitch.tv/TastySlugName");

        Assert.Equal("Twitch Clip - TastySlugName", source!.DisplayTitle());
    }

    [Fact]
    public void Names_a_live_channel_by_the_channel()
    {
        var source = TwitchSource.FromUrl("https://www.twitch.tv/somestreamer");

        Assert.Equal("somestreamer", source!.DisplayTitle());
    }
}
