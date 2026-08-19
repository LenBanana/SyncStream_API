using System;
using Microsoft.AspNetCore.Http;

namespace SyncStreamAPI.Auth;

/// <summary>
///     Issues and clears the auth cookie. One place, so the flags cannot drift apart between login,
///     refresh and logout.
/// </summary>
public static class AuthCookie
{
    public static void Issue(HttpResponse response, string token, DateTime issuedUtc)
    {
        response.Cookies.Append(TokenAuthDefaults.CookieName, token, Options(issuedUtc));
    }

    public static void Clear(HttpResponse response)
    {
        // Same attributes as when issued, otherwise the browser keeps the original cookie.
        var options = Options(DateTime.UtcNow);
        options.Expires = DateTimeOffset.UnixEpoch;
        response.Cookies.Append(TokenAuthDefaults.CookieName, string.Empty, options);
    }

    private static CookieOptions Options(DateTime issuedUtc)
    {
        return new CookieOptions
        {
            // Unreadable by script, which is the entire reason for choosing a cookie over a token held in JS.
            HttpOnly = true,

            // Lax rather than None: it still rides same-site subdomain requests such as
            // dreckbu.de -> sync.dreckbu.de, and it refuses to ride cross-site POSTs, which is CSRF
            // protection for the state-changing controllers that comes for free.
            //
            // The requirement this creates: the API must be same-site with the page. A page on drecktu.be
            // talking to sync.dreckbu.de is cross-site and would not send this cookie, so each front-end
            // origin needs an API host under its own registrable domain.
            SameSite = SameSiteMode.Lax,

            // Always set, because the app is served over HTTPS in every environment that matters and
            // localhost is exempt from the secure-origin restriction in current browsers.
            Secure = true,

            Path = "/",
            Expires = new DateTimeOffset(issuedUtc.Add(RememberTokenAuthenticationHandler.TokenLifetime))
        };
    }
}
