using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Auth;

/// <summary>
///     Named policies replacing the <c>[Privilege]</c> aspect, which read a token out of a method argument
///     by positional index and looked the user up itself.
/// </summary>
public static class AuthorizationPolicies
{
    public const string Approved = "Approved";
    public const string Moderator = "Moderator";
    public const string Administrator = "Administrator";
    public const string Elevated = "Elevated";

    public static void AddSyncStreamPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(Approved, policy => policy.RequirePrivilege(UserPrivileges.Approved));
        options.AddPolicy(Moderator, policy => policy.RequirePrivilege(UserPrivileges.Moderator));
        options.AddPolicy(Administrator, policy => policy.RequirePrivilege(UserPrivileges.Administrator));
        options.AddPolicy(Elevated, policy => policy.RequirePrivilege(UserPrivileges.Elevated));
    }

    private static void RequirePrivilege(this AuthorizationPolicyBuilder policy, UserPrivileges required)
    {
        policy.AddAuthenticationSchemes(TokenAuthDefaults.Scheme, ApiKeyAuthenticationHandler.Scheme);

        // Separates the two failures the client has to tell apart: no credential challenges (401, "sign
        // in"), a valid credential without the level forbids (403, "not allowed"). A bare assertion would
        // return 403 for both.
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            CallerIdentity.From(context.User, string.Empty).HasAtLeast(required));
    }
}
