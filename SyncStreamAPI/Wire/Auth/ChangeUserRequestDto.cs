namespace SyncStreamAPI.Wire.Auth;

/// <summary>
///     Inbound profile change. <see cref="ID" /> is still client-supplied here and is only safe
///     because the caller must also prove the current password; it moves to the connection's
///     identity when authentication moves to the connection.
/// </summary>
public class ChangeUserRequestDto
{
    public int ID { get; set; }
    public string? username { get; set; }

    /// <summary>New password as a SHA512 hash, or empty to leave it unchanged.</summary>
    public string? password { get; set; }
}
