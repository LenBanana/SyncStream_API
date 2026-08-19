namespace SyncStreamAPI.Wire.Auth;

/// <summary>
///     Inbound profile change.
/// </summary>
/// <remarks>
///     No account id: the account being changed is the authenticated one. The previous version took an
///     <c>ID</c> from the wire, which was only not exploitable because the caller also had to supply that
///     account's current password.
/// </remarks>
public class ChangeUserRequestDto
{
    /// <summary>New username, or null or unchanged to leave it alone.</summary>
    public string? username { get; set; }

    /// <summary>New password as typed, or null or empty to leave it alone.</summary>
    public string? newPassword { get; set; }

    /// <summary>The account's current password as typed. Required.</summary>
    public string? currentPassword { get; set; }
}
