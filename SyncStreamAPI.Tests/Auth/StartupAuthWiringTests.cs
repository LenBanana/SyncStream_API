using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.Auth;
using Xunit;

namespace SyncStreamAPI.Tests.Auth;

/// <summary>
///     Checks the authentication and authorization wiring in the real <see cref="Startup" />.
/// </summary>
/// <remarks>
///     A policy name that no one registered is not a compile error. <c>[Authorize(Policy = "Typo")]</c>
///     builds happily and throws the first time someone calls the method, which for an admin-only endpoint
///     could sit unnoticed for a long time. Same for a scheme that was never added.
///     <para>
///         Only <c>ConfigureServices</c> is exercised, and only auth services are resolved from it. Resolving
///         everything would construct MainManager, which downloads ffmpeg and launches a headless browser.
///     </para>
/// </remarks>
public class StartupAuthWiringTests
{
    private static ServiceProvider BuildAuthServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SyncStreamDB"] = "Host=localhost;Database=unused;Username=u;Password=p"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        new Startup(configuration).ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>Every policy name any [Authorize] attribute in the assembly asks for.</summary>
    private static IEnumerable<string> ReferencedPolicyNames()
    {
        return typeof(Startup).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance |
                                                BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>(true))
            .Select(attribute => attribute.Policy)
            .Where(policy => !string.IsNullOrEmpty(policy))
            .Select(policy => policy!)
            .Distinct();
    }

    [Fact]
    public async Task Every_policy_referenced_by_an_authorize_attribute_is_registered()
    {
        await using var provider = BuildAuthServices();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var missing = new List<string>();
        foreach (var name in ReferencedPolicyNames())
            if (await policies.GetPolicyAsync(name) == null)
                missing.Add(name);

        Assert.True(missing.Count == 0,
            "Referenced but not registered, so calling these would throw at request time: "
            + string.Join(", ", missing));
    }

    [Fact]
    public async Task The_expected_policies_exist()
    {
        await using var provider = BuildAuthServices();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var name in new[]
                 {
                     AuthorizationPolicies.Approved, AuthorizationPolicies.Moderator,
                     AuthorizationPolicies.Administrator, AuthorizationPolicies.Elevated
                 })
        {
            var policy = await policies.GetPolicyAsync(name);
            Assert.NotNull(policy);

            // Both credential kinds have to satisfy a policy, or an API-key caller would be refused.
            Assert.Contains(TokenAuthDefaults.Scheme, policy!.AuthenticationSchemes);
            Assert.Contains(ApiKeyAuthenticationHandler.Scheme, policy.AuthenticationSchemes);
        }
    }

    [Fact]
    public async Task All_three_authentication_schemes_are_registered_and_the_default_forwards()
    {
        await using var provider = BuildAuthServices();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.NotNull(await schemes.GetSchemeAsync(TokenAuthDefaults.Scheme));
        Assert.NotNull(await schemes.GetSchemeAsync(ApiKeyAuthenticationHandler.Scheme));

        // The forwarding scheme must be the default, otherwise a hub connection authenticates only from the
        // cookie and an API-key client arrives as a guest.
        var fallback = await schemes.GetDefaultAuthenticateSchemeAsync();
        Assert.NotNull(fallback);
        Assert.Equal(TokenAuthDefaults.SmartScheme, fallback!.Name);
    }

    [Fact]
    public void The_user_id_provider_is_replaced()
    {
        // Without this, Clients.User(id) silently reaches nobody.
        using var provider = BuildAuthServices();
        Assert.IsType<DbUserIdProvider>(provider.GetRequiredService<IUserIdProvider>());
    }
}
