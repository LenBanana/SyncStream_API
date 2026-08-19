using System;
using System.Security.Cryptography;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Auth;

public static class RememberTokens
{
    /// <summary>
    ///     Issues a fresh, unpredictable remember token.
    /// </summary>
    /// <remarks>
    ///     Replaces <c>Sha256(ID + username + userInfo + usersalt)</c>, which was fully deterministic: the
    ///     same account and device string always produced the same token, so a token never rotated, a
    ///     password change did not invalidate it, and logging out could not revoke it because the next login
    ///     regenerated the identical string.
    ///     <para>
    ///         Storing the token in plain text is still the pre-hardening state. Hashing tokens at rest, along
    ///         with explicit revocation and last-seen columns, needs a schema change and belongs with the
    ///         password work.
    ///     </para>
    /// </remarks>
    public static DbRememberToken Issue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return new DbRememberToken
        {
            Token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('='),
            Created = DateTime.UtcNow
        };
    }
}
