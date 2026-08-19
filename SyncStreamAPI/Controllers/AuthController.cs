using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Auth;
using SyncStreamAPI.DataContext;
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

    /// <summary>Length of a SHA512 hex digest, which is what the browser sends.</summary>
    private const int ExpectedPasswordHashLength = 128;

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
        if (user == null || !PasswordMatches(user, credentials.password))
            return Unauthorized(new { message = "Username or password is incorrect" });

        if (user.approved <= 0)
            return StatusCode(403, new { message = "This account is waiting for admin approval" });

        var token = RememberTokens.Issue();
        user.RememberTokens.Add(token);
        await _postgres.SaveChangesAsync();

        AuthCookie.Issue(Response, token.Token, token.Created);
        return Ok(user.ToSelfProfile());
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] CredentialsDto credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials?.username) || string.IsNullOrWhiteSpace(credentials.password))
            return BadRequest(new { message = "Username and password are required" });

        if (!Regex.IsMatch(credentials.username, UsernamePattern))
            return BadRequest(new { message = $"Username {credentials.username} not allowed" });

        if (credentials.password.Length != ExpectedPasswordHashLength)
            return BadRequest(new { message = "Invalid password hash" });

        if (await _postgres.Users.AnyAsync(candidate => candidate.username == credentials.username))
            return Conflict(new { message = "Username already exists" });

        // Built here, never bound from the request: approval state and privileges are the server's to
        // decide, and binding them from the wire let a caller register itself as an administrator.
        var user = new DbUser
        {
            username = credentials.username,
            password = credentials.password,
            approved = 0,
            userprivileges = UserPrivileges.NotApproved
        };
        user.StreamToken = RememberTokens.Issue().Token;

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
            // Revoke server-side as well as clearing the cookie, so a copied token stops working.
            var token = await _postgres.RememberTokens
                .FirstOrDefaultAsync(candidate => candidate.Token == presented);
            if (token != null)
            {
                _postgres.RememberTokens.Remove(token);
                await _postgres.SaveChangesAsync();
            }
        }

        AuthCookie.Clear(Response);
        return NoContent();
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

        if (!PasswordMatches(user, request.currentPassword))
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
        if (!string.IsNullOrEmpty(request.newPassword) && request.newPassword != user.password)
        {
            if (request.newPassword.Length != ExpectedPasswordHashLength)
                return BadRequest(new { message = "Invalid password hash" });

            user.password = request.newPassword;
            passwordChanged = true;
            changes.Add("Password");
        }

        if (changes.Count == 0) return Ok(new { message = "Nothing changed." });

        if (passwordChanged)
        {
            // Changing a password ends every other session, which the deterministic tokens this replaced
            // could not express: the same token string was regenerated on the next login.
            var presented = Request.Cookies[TokenAuthDefaults.CookieName];
            foreach (var stale in user.RememberTokens.Where(candidate => candidate.Token != presented).ToList())
            {
                user.RememberTokens.Remove(stale);
                _postgres.RememberTokens.Remove(stale);
            }
        }

        await _postgres.SaveChangesAsync();
        await _hub.Clients.All.getusers(_postgres.Users.ToList().ToSummaries());

        return Ok(new { message = string.Join(" & ", changes) + " successfully changed" });
    }

    /// <summary>
    ///     Compares the browser-computed SHA512 against what is stored.
    /// </summary>
    /// <remarks>
    ///     Still the pre-hardening comparison: unsalted, no server-side work factor, and the stored value is
    ///     what the client sends, so reading the table is enough to sign in as anyone. Replacing it with
    ///     Argon2id over this same input needs a schema change and is the password phase's job. It lives in
    ///     one method so that phase has one place to change.
    /// </remarks>
    private static bool PasswordMatches(DbUser user, string providedHash)
    {
        return !string.IsNullOrEmpty(user.password)
               && string.Equals(user.password, providedHash, StringComparison.Ordinal);
    }
}
