using System;
using System.Security.Cryptography;
using System.Text;

namespace SyncStreamAPI.Auth;

/// <summary>What checking a password against a stored hash concluded.</summary>
public enum PasswordVerdict
{
    /// <summary>Wrong password, or a stored value this cannot read.</summary>
    NoMatch,

    /// <summary>Correct, and stored in the current form.</summary>
    Match,

    /// <summary>
    ///     Correct, but stored in an older form that should be replaced now that the plaintext is in hand.
    /// </summary>
    MatchNeedsUpgrade
}

/// <summary>
///     Turns a password into something safe to store, and checks one against it.
/// </summary>
/// <remarks>
///     What this replaces: the browser hashed the password with SHA512 and the server stored that string
///     verbatim and compared it with string equality. Because the server accepted that same string as the
///     credential, the stored value was the credential. Reading the database was enough to authenticate as
///     anybody, with no cracking step, and the fast unsalted digest also made recovering the original password
///     cheap for anything short or common. The account's salt column existed but was never applied to it.
///     <para>
///         PBKDF2-HMAC-SHA512 at 210,000 iterations, which is the OWASP figure for this construction, using
///         the framework's own implementation so no dependency is added. The stored string names its own
///         algorithm and iteration count, so the cost can be raised, or the whole construction swapped for
///         Argon2, by writing a new format and letting <see cref="Verify" /> upgrade each account as its owner
///         signs in. There is no second migration in that.
///     </para>
/// </remarks>
public static class PasswordHashing
{
    /// <summary>Current stored form: PBKDF2 over the password itself.</summary>
    private const string CurrentAlgorithm = "PBKDF2-SHA512";

    /// <summary>
    ///     Transitional stored form: PBKDF2 over the SHA512 digest the browser used to send.
    /// </summary>
    /// <remarks>
    ///     Every account already in the database held that digest, so wrapping it is possible without anybody
    ///     signing in, which is what makes it possible to stop storing a directly replayable value immediately
    ///     rather than account by account over however long people take to come back. The server can still
    ///     check a plaintext password against it, because it can compute the same digest, so the change is
    ///     invisible from outside. Rows in this form are rewritten to <see cref="CurrentAlgorithm" /> on the
    ///     first successful sign-in, which is the first moment the plaintext exists on this side.
    /// </remarks>
    private const string PreHashedAlgorithm = "PBKDF2-SHA512-PRESHA512";

    /// <summary>OWASP's iteration count for PBKDF2-HMAC-SHA512.</summary>
    private const int Iterations = 210_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 64;

    /// <summary>Length of the SHA512 hex digest the browser used to send, and that old rows contain.</summary>
    private const int LegacyDigestLength = 128;

    /// <summary>Hashes a password for storage.</summary>
    public static string Hash(string password)
    {
        if (password == null) throw new ArgumentNullException(nameof(password));

        return Derive(CurrentAlgorithm, Encoding.UTF8.GetBytes(password), RandomNumberGenerator.GetBytes(SaltBytes),
            Iterations);
    }

    /// <summary>
    ///     Wraps a bare SHA512 digest from an old row, without needing the password it came from.
    /// </summary>
    public static string WrapLegacyDigest(string legacyDigest)
    {
        if (legacyDigest == null) throw new ArgumentNullException(nameof(legacyDigest));

        return Derive(PreHashedAlgorithm, Encoding.UTF8.GetBytes(legacyDigest.ToLowerInvariant()),
            RandomNumberGenerator.GetBytes(SaltBytes), Iterations);
    }

    /// <summary>Whether a stored value is a bare legacy digest rather than a hash this produced.</summary>
    public static bool IsLegacyDigest(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || stored.Length != LegacyDigestLength) return false;

        foreach (var character in stored)
            if (!Uri.IsHexDigit(character))
                return false;

        return true;
    }

    /// <summary>
    ///     Checks a password against a stored value.
    /// </summary>
    /// <param name="stored">Whatever the account row holds: a current hash, a wrapped digest, or a bare one.</param>
    /// <param name="password">The password as typed.</param>
    public static PasswordVerdict Verify(string? stored, string? password)
    {
        if (string.IsNullOrEmpty(stored) || password == null) return PasswordVerdict.NoMatch;

        // A row the startup pass has not reached yet, or one restored from a backup taken before it ran.
        if (IsLegacyDigest(stored))
            return FixedTimeEquals(Sha512Hex(password), stored)
                ? PasswordVerdict.MatchNeedsUpgrade
                : PasswordVerdict.NoMatch;

        var parts = stored.Split('$');
        if (parts.Length != 4) return PasswordVerdict.NoMatch;

        var algorithm = parts[0];
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return PasswordVerdict.NoMatch;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return PasswordVerdict.NoMatch;
        }

        byte[] input;
        switch (algorithm)
        {
            case CurrentAlgorithm:
                input = Encoding.UTF8.GetBytes(password);
                break;
            case PreHashedAlgorithm:
                // The digest the browser used to compute, computed here instead.
                input = Encoding.UTF8.GetBytes(Sha512Hex(password));
                break;
            default:
                return PasswordVerdict.NoMatch;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(input, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected)) return PasswordVerdict.NoMatch;

        // Anything that is not the current algorithm at the current cost is worth rewriting while the password
        // is available to rewrite it from.
        return algorithm == CurrentAlgorithm && iterations == Iterations
            ? PasswordVerdict.Match
            : PasswordVerdict.MatchNeedsUpgrade;
    }

    private static string Derive(string algorithm, byte[] input, byte[] salt, int iterations)
    {
        var hash = Rfc2898DeriveBytes.Pbkdf2(input, salt, iterations, HashAlgorithmName.SHA512, HashBytes);
        return string.Join('$', algorithm, iterations.ToString(), Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    /// <summary>The digest the browser used to send, in the lowercase hex it produced.</summary>
    private static string Sha512Hex(string value)
    {
        var digest = SHA512.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>Compares two hex strings without leaking where they first differ.</summary>
    private static bool FixedTimeEquals(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right.ToLowerInvariant()));
    }
}
