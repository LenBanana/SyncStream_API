using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Auth;

public class TokenAuthOptions : AuthenticationSchemeOptions
{
}

/// <summary>
///     Authenticates a request from the remember-token cookie.
/// </summary>
/// <remarks>
///     This is the only place a remember token is turned into an identity. Before this existed, every hub
///     method and controller action took a token as an argument and looked the user up itself, 72 methods
///     and 99 attributes over, each one an opportunity to forget the check.
///     <para>
///         Returning NoResult rather than Fail for a missing or unknown cookie is deliberate: the hub accepts
///         anonymous connections so guests can watch, and a failed result would turn every guest connection
///         into a logged authentication error.
///     </para>
/// </remarks>
public class RememberTokenAuthenticationHandler : AuthenticationHandler<TokenAuthOptions>
{
    /// <summary>
    ///     How long a session lasts when issued.
    /// </summary>
    /// <remarks>
    ///     Kept for the cookie's own expiry, which has to match. Whether a session is still valid is decided by
    ///     the expiry recorded on the row, not by this, so shortening it does not retroactively end sessions
    ///     that were issued under the previous promise.
    /// </remarks>
    public static TimeSpan TokenLifetime => RememberTokens.Lifetime;

    /// <summary>
    ///     How stale the last-used stamp is allowed to get before it is written again.
    /// </summary>
    /// <remarks>
    ///     This runs on every authenticated request, including every hub message, so recording the exact moment
    ///     each time would mean a database write per request to store something nothing needs to that precision.
    /// </remarks>
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromHours(1);

    private readonly PostgresContext _postgres;

    public RememberTokenAuthenticationHandler(
        IOptionsMonitor<TokenAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PostgresContext postgres) : base(options, logger, encoder)
    {
        _postgres = postgres;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[TokenAuthDefaults.CookieName];
        if (string.IsNullOrWhiteSpace(token)) return AuthenticateResult.NoResult();

        // Looked up by digest, because the digest is all that is stored. A stolen database yields no usable
        // cookie.
        var digest = RememberTokens.Digest(token);

        var user = await _postgres.Users
            .Include(candidate => candidate.RememberTokens)
            .FirstOrDefaultAsync(candidate =>
                candidate.RememberTokens.Any(remembered => remembered.TokenHash == digest));

        if (user == null) return AuthenticateResult.NoResult();

        var remembered = user.RememberTokens.FirstOrDefault(candidate => candidate.TokenHash == digest);
        if (remembered == null) return AuthenticateResult.NoResult();

        var now = DateTime.UtcNow;
        if (!remembered.IsUsable(now)) return AuthenticateResult.NoResult();

        // Left in place rather than deleted, so an expired or revoked session stays distinguishable from one
        // that never existed. Sweeping them is a separate job with its own schedule.
        if (remembered.LastUsedUtc == null || now - remembered.LastUsedUtc > LastUsedPrecision)
        {
            remembered.LastUsedUtc = now;
            await _postgres.SaveChangesAsync();
        }

        // An unapproved account authenticates but carries no privileges, so it can watch as a guest can
        // while every gated action still refuses it.
        return AuthenticateResult.Success(new AuthenticationTicket(BuildPrincipal(user), Scheme.Name));
    }

    private ClaimsPrincipal BuildPrincipal(DbUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ID.ToString()),
            new(SyncStreamClaims.UserId, user.ID.ToString()),
            new(SyncStreamClaims.Privilege, ((int)user.userprivileges).ToString()),
            new(SyncStreamClaims.Approved, (user.approved > 0).ToString())
        };

        if (!string.IsNullOrEmpty(user.username)) claims.Add(new Claim(ClaimTypes.Name, user.username));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
    }
}
