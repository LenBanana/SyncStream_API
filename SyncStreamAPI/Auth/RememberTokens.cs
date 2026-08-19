using System;
using System.Security.Cryptography;
using System.Text;
using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Auth;

/// <summary>
///     Issues and recognises the token behind a signed-in session.
/// </summary>
/// <remarks>
///     Replaces <c>Sha256(ID + username + userInfo + usersalt)</c>, which was fully deterministic: the same
///     account and device string always produced the same token, so a token never rotated, a password change did
///     not invalidate it, and logging out could not revoke it because the next login regenerated the identical
///     string.
///     <para>
///         The token is returned once, to be put in the cookie, and never stored. What is stored is its digest,
///         so the table is no longer worth stealing on its own.
///     </para>
/// </remarks>
public static class RememberTokens
{
    /// <summary>How long a newly issued session lasts.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>
    ///     Issues a fresh session.
    /// </summary>
    /// <returns>
    ///     The token to hand to the browser, and the record to store. The token is not recoverable from the
    ///     record, which is the point, so it has to be used before it goes out of scope.
    /// </returns>
    public static (string Token, DbRememberToken Record) Issue(DateTime nowUtc)
    {
        var token = RandomKeys.UrlSafe(32);

        var record = new DbRememberToken
        {
            TokenHash = Digest(token),
            Created = nowUtc,
            ExpiresUtc = nowUtc.Add(Lifetime)
        };

        return (token, record);
    }

    /// <summary>The stored form of a token, for looking one up.</summary>
    public static string Digest(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

}
