using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SyncStreamAPI.Helper.Streaming;

/// <summary>
///     The text subtitles and fonts a sharer's browser reads out of the file it shares, and how ffmpeg burns them in.
/// </summary>
/// <remarks>
///     ffmpeg's subtitles filter reads every subtitle line out of its file once, when it starts, and never again. A
///     share starts as soon as the first part of its upload has arrived, so the filter pointed at the upload found
///     a few seconds' worth of lines at best, and burned nothing in after them. Picture subtitles are not affected:
///     they are taken from the stream as it plays. The browser has the whole file, so it sends the text tracks and
///     the fonts they need ahead of the share, and the filter reads those instead.
/// </remarks>
public static class SubtitleSidecars
{
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public const long MaxTotalBytes = 256L * 1024 * 1024;
    public const int MaxFiles = 128;

    private static readonly string[] SubtitleExtensions = { ".ass", ".ssa", ".srt" };

    // subtitle-<n> is the subtitle track the file holds as ffmpeg's 0:s:<n>; fonts keep only their kind. \z rather
    // than $, which also matches before a trailing newline.
    private static readonly Regex AllowedName = new(@"^(subtitle-\d{1,3}\.(ass|ssa|srt)|font-\d{1,4}\.(ttf|otf|ttc))\z",
        RegexOptions.CultureInvariant);

    public static bool IsAllowedName(string? name) => name != null && AllowedName.IsMatch(name);

    /// <summary>The sidecar for the subtitle stream with this ordinal, or null when the browser sent none.</summary>
    public static string? SubtitleFor(string uploadId, int ordinal)
    {
        var directory = RoomUploadPaths.SidecarDirectoryFor(uploadId);
        return SubtitleExtensions
            .Select(extension => Path.Combine(directory, $"subtitle-{ordinal}{extension}"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Burns a text subtitle file the browser sent, with the fonts it sent next to it.</summary>
    public static string FilterForSidecar(string subtitlePath, string fontsDirectory)
        => $"subtitles='{EscapeFilterPath(subtitlePath)}':fontsdir='{EscapeFilterPath(fontsDirectory)}'";

    /// <summary>
    ///     Burns a subtitle stream straight from the media file, with the fonts attached to it. Only complete when
    ///     the whole file was there as ffmpeg started.
    /// </summary>
    public static string FilterForMedia(string mediaPath, int ordinal)
        => $"subtitles='{EscapeFilterPath(mediaPath)}':si={ordinal}";

    private static string EscapeFilterPath(string path)
        => path
            .Replace("\\", "\\\\")
            .Replace(":", "\\:")
            .Replace("'", "\\'")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace(",", "\\,");
}
