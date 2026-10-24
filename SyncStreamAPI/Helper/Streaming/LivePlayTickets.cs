using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace SyncStreamAPI.Helper.Streaming;

/// <summary>Permission to start playing one live stream, as carried in a play URL's <c>token</c>.</summary>
/// <param name="Stream">The stream name the ticket is for.</param>
/// <param name="UserId">The account it was issued to, or null for a guest.</param>
/// <param name="ExpiresAt">When it stops being accepted for starting a play.</param>
public sealed record LivePlayTicket(string Stream, int? UserId, DateTimeOffset ExpiresAt);

/// <summary>
///     Issues and reads the tickets the streaming server's play callback checks.
/// </summary>
/// <remarks>
///     nginx asks the API whether to let a play through and hands over nothing but the play URL's query string.
///     That used to carry the viewer's remember token, which stopped working when the session moved into an
///     HttpOnly cookie: the page can no longer read it, so every play was refused. Putting a session credential
///     in a URL was wrong anyway. A ticket names one stream, expires within minutes and is signed with a key that
///     exists only in this process, so it is worthless for anything but starting that play.
/// </remarks>
public sealed class LivePlayTickets
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Issue(string stream, int? userId, DateTimeOffset expiresAt)
    {
        var payload = string.Join('.',
            WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(stream)),
            userId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return $"{payload}.{WebEncoders.Base64UrlEncode(Sign(payload))}";
    }

    /// <summary>
    ///     Reads a ticket this process issued. Expiry is not checked here: a play that ends is reported with the
    ///     ticket it started with, long after that ticket stopped being good for starting one.
    /// </summary>
    public bool TryRead(string? token, out LivePlayTicket ticket)
    {
        ticket = null!;
        if (string.IsNullOrEmpty(token)) return false;

        var lastDot = token.LastIndexOf('.');
        if (lastDot <= 0) return false;
        var payload = token[..lastDot];

        if (!TryDecode(token[(lastDot + 1)..], out var signature) ||
            !CryptographicOperations.FixedTimeEquals(signature, Sign(payload)))
            return false;

        var parts = payload.Split('.');
        if (parts.Length != 3 || !TryDecode(parts[0], out var streamBytes)) return false;

        int? userId = null;
        if (parts[1].Length > 0)
        {
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return false;
            userId = id;
        }

        if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAtUnix))
            return false;

        ticket = new LivePlayTicket(Encoding.UTF8.GetString(streamBytes), userId,
            DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix));
        return true;
    }

    private byte[] Sign(string payload) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));

    private static bool TryDecode(string text, out byte[] bytes)
    {
        try
        {
            bytes = WebEncoders.Base64UrlDecode(text);
            return true;
        }
        catch (FormatException)
        {
            // Part of a token someone typed or altered: not a ticket, which is what the caller is told.
            bytes = Array.Empty<byte>();
            return false;
        }
    }
}
