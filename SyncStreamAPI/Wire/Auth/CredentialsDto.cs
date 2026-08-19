namespace SyncStreamAPI.Wire.Auth;

/// <summary>
///     Inbound credentials for login and registration. Deliberately carries nothing else:
///     binding an entity here is what allowed a client to register itself as an administrator by
///     setting <c>approved</c> and <c>userprivileges</c> on the wire.
/// </summary>
public class CredentialsDto
{
    public string? username { get; set; }

    /// <summary>
    ///     The password as typed.
    /// </summary>
    /// <remarks>
    ///     Used to be a SHA512 digest computed in the browser, which the server then stored and compared
    ///     verbatim. That made the stored value the credential: whoever read the table could send it back. The
    ///     server hashes this before storing it and never keeps it.
    /// </remarks>
    public string? password { get; set; }
}
