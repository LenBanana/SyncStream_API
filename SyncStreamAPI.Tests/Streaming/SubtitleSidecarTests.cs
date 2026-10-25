using SyncStreamAPI.Helper.Streaming;
using Xunit;

namespace SyncStreamAPI.Tests.Streaming;

/// <summary>
///     A sharer's browser writes files next to its upload that ffmpeg then reads, so only the two kinds it has a
///     reason to send are accepted, under names that cannot reach outside the upload's folder.
/// </summary>
public class SubtitleSidecarTests
{
    [Theory]
    [InlineData("subtitle-0.ass")]
    [InlineData("subtitle-12.ssa")]
    [InlineData("subtitle-3.srt")]
    [InlineData("font-0.ttf")]
    [InlineData("font-1234.otf")]
    [InlineData("font-7.ttc")]
    public void Subtitles_and_fonts_are_accepted(string name)
    {
        Assert.True(SubtitleSidecars.IsAllowedName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("upload.bin")]
    [InlineData("session.json")]
    [InlineData("../session.json")]
    [InlineData("subtitle-0.ass/../../x")]
    [InlineData("subtitle-x.ass")]
    [InlineData("subtitle-0.ass.partial")]
    [InlineData("font-0.exe")]
    [InlineData("SUBTITLE-0.ASS")]
    [InlineData("subtitle-0.ass\n")]
    public void Anything_else_is_refused(string? name)
    {
        Assert.False(SubtitleSidecars.IsAllowedName(name));
    }

    [Fact]
    public void A_sent_subtitle_file_is_burned_with_the_fonts_sent_beside_it()
    {
        Assert.Equal(@"subtitles='/data/.uploads/abc/sidecar/subtitle-3.ass':fontsdir='/data/.uploads/abc/sidecar'",
            SubtitleSidecars.FilterForSidecar("/data/.uploads/abc/sidecar/subtitle-3.ass", "/data/.uploads/abc/sidecar"));
    }

    [Fact]
    public void Without_one_the_stream_is_read_from_the_media_file()
    {
        Assert.Equal(@"subtitles='/data/upload.bin':si=2", SubtitleSidecars.FilterForMedia("/data/upload.bin", 2));
    }

    [Fact]
    public void Characters_the_filter_syntax_uses_are_escaped_in_paths()
    {
        Assert.Equal(@"subtitles='C\:\\a\'b\[1\]\,c.ass':fontsdir='x'",
            SubtitleSidecars.FilterForSidecar(@"C:\a'b[1],c.ass", "x"));
    }
}
