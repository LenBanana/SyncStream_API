using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Auth;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Wire;
using SyncStreamAPI.Wire.Auth;
using SyncStreamAPI.Wire.Users;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;

namespace SyncStreamAPI.Controllers;

/// <summary>
///     Login, registration and logout.
/// </summary>
/// <remarks>
///     These were hub methods. They are HTTP endpoints because the session lives in an HttpOnly cookie, and a
///     cookie can only be set on an HTTP response: there is no response to attach one to from inside a hub
///     invocation. Authenticating the transport once is also the point of the change, so proving identity
///     over the hub on every call is exactly what is being removed.
/// </remarks>
[ApiController]
[Route("api/auth")]
[EnableCors("CORSPolicy")]
public class AuthController : ControllerBase
{
    private const string UsernamePattern = @"^\w(?:\w|.-){2,31}$";

    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly PostgresContext _postgres;

    public AuthController(PostgresContext postgres, IHubContext<ServerHub, IServerHub> hub)
    {
        _postgres = postgres;
        _hub = hub;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] CredentialsDto credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials?.username) || string.IsNullOrWhiteSpace(credentials.password))
            return BadRequest(new { message = "Username and password are required" });

        var user = await _postgres.Users
            .Include(candidate => candidate.RememberTokens)
            .FirstOrDefaultAsync(candidate => candidate.username == credentials.username);

        // One message and one code for "no such account" and "wrong password", so the endpoint cannot be
        // used to enumerate usernames.
        var verdict = user == null
            ? PasswordVerdict.NoMatch
            : PasswordHashing.Verify(user.password, credentials.password);

        if (user == null || verdict == PasswordVerdict.NoMatch)
            return Unauthorized(new { message = "Username or password is incorrect" });

        if (user.approved <= 0)
            return StatusCode(403, new { message = "This account is waiting for admin approval" });

        // This is the only moment the plaintext exists on this side, so it is the only moment an account still
        // stored in an older form can be rewritten into the current one. Nobody is prompted and nobody is
        // signed out; the row simply stops being in the old shape.
        if (verdict == PasswordVerdict.MatchNeedsUpgrade) user.password = PasswordHashing.Hash(credentials.password);

        var now = DateTime.UtcNow;
        var (token, record) = RememberTokens.Issue(now);
        user.RememberTokens.Add(record);
        await _postgres.SaveChangesAsync();

        AuthCookie.Issue(Response, token, record.Created);
        return Ok(user.ToSelfProfile());
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] CredentialsDto credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials?.username) || string.IsNullOrWhiteSpace(credentials.password))
            return BadRequest(new { message = "Username and password are required" });

        if (!Regex.IsMatch(credentials.username, UsernamePattern))
            return BadRequest(new { message = $"Username {credentials.username} not allowed" });

        if (!PasswordPolicy.IsAcceptable(credentials.password, out var rejection))
            return BadRequest(new { message = rejection });

        if (await _postgres.Users.AnyAsync(candidate => candidate.username == credentials.username))
            return Conflict(new { message = "Username already exists" });

        // Built here, never bound from the request: approval state and privileges are the server's to
        // decide, and binding them from the wire let a caller register itself as an administrator.
        var user = new DbUser
        {
            username = credentials.username,
            password = PasswordHashing.Hash(credentials.password),
            approved = 0,
            userprivileges = UserPrivileges.NotApproved
        };

        await _postgres.Users.AddAsync(user);
        await _postgres.SaveChangesAsync();

        // No cookie is issued: the account cannot be used until an admin approves it.
        return Ok(user.ToSelfProfile());
    }

    /// <summary>Who the caller is, or 204 when nobody. Used on page load to restore a session.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var caller = CallerIdentity.From(User, string.Empty);
        if (caller.IsGuest) return NoContent();

        var user = await _postgres.Users.FirstOrDefaultAsync(candidate => candidate.ID == caller.UserId);
        if (user == null)
        {
            AuthCookie.Clear(Response);
            return NoContent();
        }

        return Ok(user.ToSelfProfile());
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var presented = Request.Cookies[TokenAuthDefaults.CookieName];
        if (!string.IsNullOrWhiteSpace(presented))
        {
            // Revoke server-side as well as clearing the cookie, so a copied token stops working. Marked
            // rather than deleted, so a token presented afterwards is recognisably a revoked session rather
            // than an unknown one.
            var digest = RememberTokens.Digest(presented);
            var token = await _postgres.RememberTokens
                .FirstOrDefaultAsync(candidate => candidate.TokenHash == digest);
            if (token is { RevokedUtc: null })
            {
                token.RevokedUtc = DateTime.UtcNow;
                await _postgres.SaveChangesAsync();
            }
        }

        AuthCookie.Clear(Response);
        return NoContent();
    }

    /// <summary>Issues a fresh API key for the authenticated account and returns the updated profile.</summary>
    /// <remarks>
    ///     Was a hub method that answered with a <c>userlogin</c> event. Nothing listened for that event once
    ///     login moved off the hub, so the key was created and never shown. A request that changes your own
    ///     credentials should answer in its own response anyway.
    /// </remarks>
    [HttpPost("api-key")]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<IActionResult> GenerateApiKey()
    {
        var user = await _postgres.Users.FirstOrDefaultAsync(candidate => candidate.ID == this.CallerUserId());
        if (user == null) return Unauthorized(new { message = "Not signed in" });

        // A readable key, not a session token: this one is displayed so it can be pasted into a client, which
        // means it cannot be stored as a digest the way the cookie's token is.
        user.ApiKey = RandomKeys.Readable();
        await _postgres.SaveChangesAsync();

        return Ok(user.ToSelfProfile());
    }

    /// <summary>Changes the authenticated account's username and/or password.</summary>
    [HttpPost("change-user")]
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task<IActionResult> ChangeUser([FromBody] ChangeUserRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request?.currentPassword))
            return BadRequest(new { message = "Your current password is required" });

        var caller = CallerIdentity.From(User, string.Empty);
        var user = await _postgres.Users
            .Include(candidate => candidate.RememberTokens)
            .FirstOrDefaultAsync(candidate => candidate.ID == caller.UserId);

        if (user == null) return Unauthorized(new { message = "Not signed in" });

        var currentVerdict = PasswordHashing.Verify(user.password, request.currentPassword);
        if (currentVerdict == PasswordVerdict.NoMatch)
            return Unauthorized(new { message = "Your current password was not correct" });

        var changes = new List<string>();

        var newUsername = request.username?.Trim();
        if (!string.IsNullOrEmpty(newUsername) && newUsername != user.username)
        {
            // Validates the incoming name. The previous version measured the *existing* username, so the
            // length rule never applied to what was actually being set.
            if (newUsername.Length is < 2 or > 20)
                return BadRequest(new { message = "Username must be between 2 and 20 characters" });

            if (await _postgres.Users.AnyAsync(candidate => candidate.username == newUsername))
                return Conflict(new { message = "Username already exists" });

            user.username = newUsername;
            changes.Add("Username");
        }

        var passwordChanged = false;
        if (!string.IsNullOrEmpty(request.newPassword))
        {
            if (!PasswordPolicy.IsAcceptable(request.newPassword, out var rejection))
                return BadRequest(new { message = rejection });

            // Compared against the current password rather than against the stored hash, which the previous
            // version did: with a per-password salt, no two hashes of the same password are equal, so that
            // comparison could never have detected "the same password again".
            if (PasswordHashing.Verify(user.password, request.newPassword) == PasswordVerdict.NoMatch)
            {
                user.password = PasswordHashing.Hash(request.newPassword);
                passwordChanged = true;
                changes.Add("Password");
            }
        }

        // Nothing else about the account changed, but the stored form is out of date and the plaintext that was
        // just verified is the only thing that can bring it up to date.
        var rewroteStoredForm = false;
        if (!passwordChanged && currentVerdict == PasswordVerdict.MatchNeedsUpgrade)
        {
            user.password = PasswordHashing.Hash(request.currentPassword);
            rewroteStoredForm = true;
        }

        if (changes.Count == 0)
        {
            // Saved even though the caller asked for no change, because the line above rewrote the stored form
            // and returning without saving would throw that away and leave the row in the old shape.
            if (rewroteStoredForm) await _postgres.SaveChangesAsync();
            return Ok(new { message = "Nothing changed." });
        }

        if (passwordChanged)
        {
            // Changing a password ends every other session, which the deterministic tokens this replaced
            // could not express: the same token string was regenerated on the next login.
            var presented = Request.Cookies[TokenAuthDefaults.CookieName];
            var keep = string.IsNullOrWhiteSpace(presented) ? null : RememberTokens.Digest(presented);
            var now = DateTime.UtcNow;
            foreach (var stale in user.RememberTokens.Where(candidate =>
                         candidate.TokenHash != keep && candidate.RevokedUtc == null))
                stale.RevokedUtc = now;
        }

        await _postgres.SaveChangesAsync();
        await _hub.Clients.All.getusers(_postgres.Users.ToList().ToSummaries());

        return Ok(new { message = string.Join(" & ", changes) + " successfully changed" });
    }

}
