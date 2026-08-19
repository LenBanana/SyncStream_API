namespace SyncStreamAPI.Wire.Auth;

/// <summary>
///     Inbound credentials for login and registration. Deliberately carries nothing else:
///     binding an entity here is what allowed a client to register itself as an administrator by
///     setting <c>approved</c> and <c>userprivileges</c> on the wire.
/// </summary>
public class CredentialsDto
{
    public string? username { get; set; }

    /// <summary>SHA512 of the plaintext, computed in the browser. Never the plaintext itself.</summary>
    public string? password { get; set; }
}
