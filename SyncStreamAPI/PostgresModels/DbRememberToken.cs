using System;

namespace SyncStreamAPI.PostgresModels;

/// <summary>
///     A signed-in session.
/// </summary>
/// <remarks>
///     Held the token itself, so reading this table was a thirty-day session takeover on every account at once.
///     It now holds only a digest of the token, which is enough to recognise the one the browser presents and
///     not enough to produce it.
///     <para>
///         A plain SHA-256 rather than the password hash: the token is 256 bits straight from the system's
///         random source, so there is no guessable input to slow an attacker down over, and this digest is
///         computed on every authenticated request where a deliberately slow one would be felt.
///     </para>
/// </remarks>
public class DbRememberToken
{
    public DbRememberToken()
    {
        Created = DateTime.UtcNow;
    }

    public int ID { get; set; }

    /// <summary>Hex SHA-256 of the token. The token itself is never stored.</summary>
    public string TokenHash { get; set; }

    public DateTime Created { get; set; }

    /// <summary>
    ///     When this session stops being accepted.
    /// </summary>
    /// <remarks>
    ///     Recorded rather than derived from <see cref="Created" /> and a constant in code, so shortening the
    ///     lifetime later does not retroactively expire sessions that were issued under the old promise, and so
    ///     a session can be given its own lifetime without a second concept.
    /// </remarks>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>When the token was last presented, or null if never since it was issued.</summary>
    public DateTime? LastUsedUtc { get; set; }

    /// <summary>
    ///     When the session was ended, or null while it is live.
    /// </summary>
    /// <remarks>
    ///     Signing out and changing a password used to delete rows. Marking them instead means "this session
    ///     was ended" is distinguishable from "this session never existed", which is what makes it possible to
    ///     show somebody their sessions and to tell whether a token that turns up later was revoked or forged.
    /// </remarks>
    public DateTime? RevokedUtc { get; set; }

    /// <summary>Whether this session may still authenticate.</summary>
    public bool IsUsable(DateTime nowUtc)
    {
        return RevokedUtc == null && nowUtc < ExpiresUtc;
    }
}
