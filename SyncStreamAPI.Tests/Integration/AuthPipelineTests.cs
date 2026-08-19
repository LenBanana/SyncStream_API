using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SyncStreamAPI.Auth;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.PostgresModels;
using Xunit;

namespace SyncStreamAPI.Tests.Integration;

/// <summary>
///     Runs the real authentication pipeline against a real database.
/// </summary>
/// <remarks>
///     The unit tests cover claims-to-caller mapping in isolation, which is not the same as knowing the
///     wiring works. This builds the same schemes, the same policies and the same middleware order the app
///     registers, over a throwaway database, and drives it with HTTP. What it is really checking is the
///     things a compiler cannot: that a cookie is accepted, that an API key header is accepted through the
///     forwarding scheme, and that the two failure modes come back as different status codes.
///     <para>
///         Skipped unless <c>SYNCSTREAM_TEST_DB</c> names a server the test may create and drop databases on.
///     </para>
/// </remarks>
public class AuthPipelineTests : IAsyncLifetime
{
    private string _database = string.Empty;
    private IHost? _host;

    private static string? ServerConnectionString => Environment.GetEnvironmentVariable("SYNCSTREAM_TEST_DB");

    private static bool Enabled => !string.IsNullOrWhiteSpace(ServerConnectionString);

    public async Task InitializeAsync()
    {
        if (!Enabled) return;

        _database = $"syncstream_authtest_{Guid.NewGuid():N}"[..40];

        using (var context = ContextFor(_database))
        {
            await context.Database.MigrateAsync();
        }

        _host = await BuildHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        if (Enabled && !string.IsNullOrEmpty(_database))
        {
            NpgsqlConnection.ClearAllPools();
            using var connection = new NpgsqlConnection(ConnectionStringFor("postgres"));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);";
            command.ExecuteNonQuery();
        }
    }

    private static string ConnectionStringFor(string database)
    {
        return new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = database }.ToString();
    }

    private static PostgresContext ContextFor(string database)
    {
        return new PostgresContext(new DbContextOptionsBuilder<PostgresContext>()
            .UseNpgsql(ConnectionStringFor(database))
            .Options);
    }

    /// <summary>
    ///     The auth wiring exactly as Startup registers it, and nothing else. Booting the real Startup would
    ///     download ffmpeg and launch a headless browser, neither of which has anything to do with auth.
    /// </summary>
    private async Task<IHost> BuildHostAsync()
    {
        var connectionString = ConnectionStringFor(_database);

        return await new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(services =>
                {
                    services.AddDbContext<PostgresContext>(options => options.UseNpgsql(connectionString));

                    services.AddAuthentication(TokenAuthDefaults.SmartScheme)
                        .AddPolicyScheme(TokenAuthDefaults.SmartScheme, TokenAuthDefaults.SmartScheme, options =>
                        {
                            options.ForwardDefaultSelector = context =>
                                context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                                    ? ApiKeyAuthenticationHandler.Scheme
                                    : TokenAuthDefaults.Scheme;
                        })
                        .AddScheme<TokenAuthOptions, RememberTokenAuthenticationHandler>(
                            TokenAuthDefaults.Scheme, null)
                        .AddScheme<ApiKeyAuthOptions, ApiKeyAuthenticationHandler>(
                            ApiKeyAuthenticationHandler.Scheme, null);

                    services.AddAuthorization(options => options.AddSyncStreamPolicies());
                    services.AddRouting();
                });
                builder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        // Reports who the pipeline decided the caller is.
                        endpoints.MapGet("/whoami", context =>
                        {
                            var caller = CallerIdentity.From(context.User, string.Empty);
                            return context.Response.WriteAsync(caller.IsGuest
                                ? "guest"
                                : $"{caller.UserId}:{caller.AccountName}:{(int)caller.Privileges}");
                        });

                        endpoints.MapGet("/needs-approved", context =>
                                context.Response.WriteAsync("ok"))
                            .RequireAuthorization(AuthorizationPolicies.Approved);

                        endpoints.MapGet("/needs-admin", context =>
                                context.Response.WriteAsync("ok"))
                            .RequireAuthorization(AuthorizationPolicies.Administrator);
                    });
                });
            })
            .StartAsync();
    }

    private HttpClient Client()
    {
        return _host!.GetTestClient();
    }

    private async Task<(DbUser user, string token)> SeedUserAsync(
        UserPrivileges privileges,
        int approved,
        string? apiKey = null,
        DateTime? tokenCreated = null)
    {
        await using var context = ContextFor(_database);

        var token = RememberTokens.Issue();
        if (tokenCreated != null) token.Created = tokenCreated.Value;

        var user = new DbUser
        {
            username = $"user_{Guid.NewGuid():N}"[..12],
            password = new string('a', 128),
            approved = approved,
            userprivileges = privileges,
            ApiKey = apiKey
        };
        user.RememberTokens.Add(token);

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return (user, token.Token);
    }

    private static HttpRequestMessage WithCookie(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"{TokenAuthDefaults.CookieName}={token}");
        return request;
    }

    [SkippableFact]
    public async Task No_credential_is_a_guest_and_a_guarded_endpoint_challenges()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_TEST_DB to run auth pipeline tests.");

        var client = Client();

        Assert.Equal("guest", await client.GetStringAsync("/whoami"));

        var guarded = await client.GetAsync("/needs-approved");
        // 401, not 403: the client has to be able to tell "sign in" from "not allowed".
        Assert.Equal(HttpStatusCode.Unauthorized, guarded.StatusCode);
    }

    [SkippableFact]
    public async Task A_valid_cookie_authenticates_and_passes_a_policy_it_meets()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_TEST_DB to run auth pipeline tests.");

        var (user, token) = await SeedUserAsync(UserPrivileges.Approved, 1);
        var client = Client();

        var whoami = await client.SendAsync(WithCookie("/whoami", token));
        Assert.Equal($"{user.ID}:{user.username}:{(int)UserPrivileges.Approved}",
            await whoami.Content.ReadAsStringAsync());

        var allowed = await client.SendAsync(WithCookie("/needs-approved", token));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        var refused = await client.SendAsync(WithCookie("/needs-admin", token));
        // Authenticated but not privileged enough, so forbidden rather than unauthorized.
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [SkippableFact]
    public async Task An_api_key_header_authenticates_through_the_forwarding_scheme()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_TEST_DB to run auth pipeline tests.");

        // This is the case the forwarding scheme exists for. With the cookie scheme as the plain default, a
        // caller presenting only an API key reached the pipeline unauthenticated.
        var apiKey = RememberTokens.Issue().Token;
        var (user, _) = await SeedUserAsync(UserPrivileges.Administrator, 1, apiKey);

        var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add(ApiKeyAuthenticationHandler.HeaderName, apiKey);

        var whoami = await Client().SendAsync(request);
        Assert.Equal($"{user.ID}:{user.username}:{(int)UserPrivileges.Administrator}",
            await whoami.Content.ReadAsStringAsync());

        var guarded = new HttpRequestMessage(HttpMethod.Get, "/needs-admin");
        guarded.Headers.Add(ApiKeyAuthenticationHandler.HeaderName, apiKey);
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(guarded)).StatusCode);
    }

    [SkippableFact]
    public async Task An_unapproved_account_authenticates_but_holds_no_privileges()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_TEST_DB to run auth pipeline tests.");

        var (_, token) = await SeedUserAsync(UserPrivileges.Elevated, 0);

        var guarded = await Client().SendAsync(WithCookie("/needs-approved", token));
        Assert.Equal(HttpStatusCode.Forbidden, guarded.StatusCode);
    }

    [SkippableFact]
    public async Task An_unknown_cookie_is_a_guest()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_TEST_DB to run auth pipeline tests.");

        var whoami = await Client().SendAsync(WithCookie("/whoami", "not-a-real-token"));
        Assert.Equal("guest", await whoami.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task An_expired_token_is_a_guest_and_is_cleaned_up()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_TEST_DB to run auth pipeline tests.");

        var expired = DateTime.UtcNow - RememberTokenAuthenticationHandler.TokenLifetime - TimeSpan.FromDays(1);
        var (_, token) = await SeedUserAsync(UserPrivileges.Approved, 1, tokenCreated: expired);

        var whoami = await Client().SendAsync(WithCookie("/whoami", token));
        Assert.Equal("guest", await whoami.Content.ReadAsStringAsync());

        await using var context = ContextFor(_database);
        Assert.False(await context.RememberTokens.AnyAsync(candidate => candidate.Token == token));
    }
}
