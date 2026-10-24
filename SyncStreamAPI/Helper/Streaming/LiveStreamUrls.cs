using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.WebUtilities;

namespace SyncStreamAPI.Helper.Streaming;

/// <summary>Reading and building the URLs live streams are played from.</summary>
public static class LiveStreamUrls
{
    private const int DefaultRtmpPort = 1935;

    /// <summary>
    ///     The stream a room's live video points at: the <c>stream</c> parameter of an HTTP-FLV URL, or the last
    ///     path segment of an RTMP one. Null when the URL names no stream.
    /// </summary>
    public static string? StreamNameOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("stream", out var values) &&
            !string.IsNullOrWhiteSpace(values.ToString()))
            return values.ToString();

        if (!uri.Scheme.StartsWith("rtmp", StringComparison.OrdinalIgnoreCase)) return null;
        var last = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrWhiteSpace(last) ? null : Uri.UnescapeDataString(last);
    }

    /// <summary>
    ///     The HTTP-FLV URL for a stream. The streaming server's HTTP side needs to be told which RTMP application
    ///     and port the stream lives on; both come from the RTMP URL the API publishes to, which is the same
    ///     application OBS publishes to.
    /// </summary>
    public static string PlayUrl(string liveBaseUrl, string rtmpUrl, string streamName, string ticket)
    {
        var rtmp = new Uri(rtmpUrl);
        var port = rtmp.Port > 0 ? rtmp.Port : DefaultRtmpPort;
        return QueryHelpers.AddQueryString(liveBaseUrl, new Dictionary<string, string?>
        {
            ["port"] = port.ToString(CultureInfo.InvariantCulture),
            ["app"] = rtmp.AbsolutePath.Trim('/'),
            ["stream"] = streamName,
            ["token"] = ticket,
        });
    }
}
