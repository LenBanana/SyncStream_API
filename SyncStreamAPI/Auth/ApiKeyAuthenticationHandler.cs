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

namespace SyncStreamAPI.Auth;

public class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
}

/// <summary>
///     Authenticates a programmatic caller from an API key header.
/// </summary>
/// <remarks>
///     For callers with no browser session: a publishing script, the browser extension. Browser-driven
///     features use the session cookie instead, so the key does not need to travel from a page.
///     <para>
///         A header rather than the query string it used to be read from. A credential in a URL is written to
///         every access log it passes through, and shows up in referrers and browser history.
///     </para>
/// </remarks>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    public const string Scheme = "SyncStreamApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly PostgresContext _postgres;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PostgresContext postgres) : base(options, logger, encoder)
    {
        _postgres = postgres;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var presented)) return AuthenticateResult.NoResult();

        var apiKey = presented.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey)) return AuthenticateResult.NoResult();

        var user = await _postgres.Users.FirstOrDefaultAsync(candidate => candidate.ApiKey == apiKey);
        if (user == null) return AuthenticateResult.Fail("Unknown API key");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ID.ToString()),
            new(SyncStreamClaims.UserId, user.ID.ToString()),
            new(SyncStreamClaims.Privilege, ((int)user.userprivileges).ToString()),
            new(SyncStreamClaims.Approved, (user.approved > 0).ToString())
        };

        if (!string.IsNullOrEmpty(user.username)) claims.Add(new Claim(ClaimTypes.Name, user.username));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
    }
}
