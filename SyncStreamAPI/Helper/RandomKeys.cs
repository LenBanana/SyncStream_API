using System;
using System.Security.Cryptography;

namespace SyncStreamAPI.Helper;

/// <summary>
///     Unguessable identifiers.
/// </summary>
/// <remarks>
///     What this replaces: a file key was <c>Sha256(userId + username + guid + name + salt)</c> and a stream key
///     was <c>MD5(guid)</c>, both routed through a helper that built a whole session-token entity just to read
///     one string off it. Neither was predictable, so neither was a hole, but hashing a random value adds
///     nothing over taking a random value, and running keys through the session-token type coupled them to a
///     table whose shape has nothing to do with them.
/// </remarks>
public static class RandomKeys
{
    /// <summary>
    ///     A key that stays readable, for values shown to their owner.
    /// </summary>
    /// <remarks>
    ///     Hex rather than base64, because these end up in URLs and in a stream key someone copies by hand,
    ///     where case sensitivity and padding characters cause support questions. 128 bits.
    /// </remarks>
    public static string Readable()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    /// <summary>A URL-safe secret, for values that travel in a cookie or a header.</summary>
    public static string UrlSafe(int bytes)
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
