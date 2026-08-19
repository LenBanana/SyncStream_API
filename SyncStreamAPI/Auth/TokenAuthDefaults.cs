namespace SyncStreamAPI.Auth;

public static class TokenAuthDefaults
{
    public const string Scheme = "SyncStreamRememberToken";

    /// <summary>
    ///     Name of the cookie carrying the remember token.
    /// </summary>
    /// <remarks>
    ///     A cookie rather than a value passed to <c>accessTokenFactory</c>, because browsers cannot set
    ///     custom headers on a WebSocket handshake: SignalR therefore falls back to putting the token in the
    ///     query string as <c>?access_token=</c>, which puts a live credential into every access log. The
    ///     cookie is sent automatically on both the negotiate POST and the socket handshake, and being
    ///     HttpOnly it is unreadable by script, so an XSS cannot exfiltrate it.
    /// </remarks>
    public const string CookieName = "syncstream-auth";
}
