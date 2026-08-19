using System;
using System.Text.RegularExpressions;

namespace SyncStreamAPI.Models;

public enum TwitchSourceKind
{
    Channel,
    Video,
    Clip
}

/// <summary>
///     What a Twitch link points at, as far as naming it in a playlist requires.
/// </summary>
/// <remarks>
///     Deliberately not the authority on how to play the link. The browser decides that, because it is the side
///     holding the embed, and it parses the link itself. This exists only to put something readable in the
///     playlist, so the worst a disagreement here can cause is an ugly title rather than a video that will not
///     load. The url is left exactly as it was given for the same reason: the previous code meant to replace it
///     with the extracted id and the line that did so assigned the field to itself, so nothing was extracted,
///     and the browser then received a whole URL where it expected an id.
/// </remarks>
public class TwitchSource
{
    private static readonly Regex VideoPath = new(@"^/videos/(\d+)/?$", RegexOptions.Compiled);

    private static readonly Regex ChannelClipPath =
        new(@"^/([A-Za-z0-9_]{1,40})/clip/([A-Za-z0-9_-]+)/?$", RegexOptions.Compiled);

    private static readonly Regex SingleSegmentPath = new(@"^/([A-Za-z0-9_-]{1,40})/?$", RegexOptions.Compiled);

    private TwitchSource(TwitchSourceKind kind, string id)
    {
        Kind = kind;
        Id = id;
    }

    public TwitchSourceKind Kind { get; }

    /// <summary>Channel login, video id, or clip slug, depending on <see cref="Kind" />.</summary>
    public string Id { get; }

    /// <summary>Reads a Twitch link, or returns null when it is not one this recognises.</summary>
    public static TwitchSource? FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var candidate = url.Trim();
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            candidate = "https://" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        if (host.StartsWith("m.")) host = host[2..];

        var path = uri.AbsolutePath;

        if (host == "clips.twitch.tv")
        {
            var embedded = GetQueryValue(uri.Query, "clip");
            if (!string.IsNullOrEmpty(embedded)) return new TwitchSource(TwitchSourceKind.Clip, embedded!);

            var onHost = SingleSegmentPath.Match(path);
            return onHost.Success ? new TwitchSource(TwitchSourceKind.Clip, onHost.Groups[1].Value) : null;
        }

        if (host != "twitch.tv") return null;

        var video = VideoPath.Match(path);
        if (video.Success) return new TwitchSource(TwitchSourceKind.Video, video.Groups[1].Value);

        var channelClip = ChannelClipPath.Match(path);
        if (channelClip.Success) return new TwitchSource(TwitchSourceKind.Clip, channelClip.Groups[2].Value);

        var channel = SingleSegmentPath.Match(path);
        return channel.Success ? new TwitchSource(TwitchSourceKind.Channel, channel.Groups[1].Value) : null;
    }

    /// <summary>
    ///     A playlist entry name.
    /// </summary>
    /// <remarks>
    ///     The prefixed forms used to interpolate the entire URL, so an entry read "Twitch Video - https://..."
    ///     rather than naming the recording.
    /// </remarks>
    public string DisplayTitle()
    {
        return Kind switch
        {
            TwitchSourceKind.Video => $"Twitch Video - {Id}",
            TwitchSourceKind.Clip => $"Twitch Clip - {Id}",
            TwitchSourceKind.Channel => Id,
            _ => Id
        };
    }

    private static string? GetQueryValue(string query, string key)
    {
        if (string.IsNullOrEmpty(query)) return null;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) continue;
            if (!pair[..separator].Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

            return Uri.UnescapeDataString(pair[(separator + 1)..]);
        }

        return null;
    }
}
