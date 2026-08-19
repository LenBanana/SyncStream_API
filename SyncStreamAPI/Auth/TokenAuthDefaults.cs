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

    /// <summary>
    ///     The default scheme, which forwards to whichever credential the request actually carries.
    /// </summary>
    /// <remarks>
    ///     Needed because a hub connection's <c>Context.User</c> is established once, at the handshake, from
    ///     the default scheme alone. Policy evaluation authenticates every scheme a policy lists, so
    ///     controllers were fine either way, but a programmatic client presenting an API key header would
    ///     have arrived at the hub unauthenticated and been refused by its own [Authorize] attribute.
    /// </remarks>
    public const string SmartScheme = "SyncStreamSmart";
}
