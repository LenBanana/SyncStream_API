namespace SyncStreamAPI.Auth;

/// <summary>
///     What counts as an acceptable password.
/// </summary>
/// <remarks>
///     Nothing checked this before, and nothing could: the browser hashed the password and the server only ever
///     saw a 128-character digest, so every password looked identical in length and content from here. The
///     length check that existed was on the digest, which told you the browser had run, not that the person had
///     chosen anything.
///     <para>
///         Deliberately no composition rules. NIST dropped the advice to demand mixed character classes because
///         it pushes people toward predictable substitutions without adding real entropy; length is what helps.
///         The upper bound is there so a request cannot ask the server to run PBKDF2 over a megabyte of input.
///     </para>
/// </remarks>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 256;

    /// <summary>Whether a password may be used, with the reason when it may not.</summary>
    public static bool IsAcceptable(string? password, out string reason)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            reason = "A password is required";
            return false;
        }

        if (password.Length < MinimumLength)
        {
            reason = $"Password must be at least {MinimumLength} characters";
            return false;
        }

        if (password.Length > MaximumLength)
        {
            reason = $"Password must be at most {MaximumLength} characters";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
