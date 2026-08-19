using System.Collections.Generic;
using System.Security.Claims;
using SyncStreamAPI.Auth;
using SyncStreamAPI.PostgresModels;
using Xunit;

namespace SyncStreamAPI.Tests.Auth;

/// <summary>
///     How a set of claims becomes a caller, since every privilege decision now reads from this.
/// </summary>
public class CallerIdentityTests
{
    private static ClaimsPrincipal Principal(UserPrivileges privileges, bool approved, int userId = 7)
    {
        var claims = new List<Claim>
        {
            new(SyncStreamClaims.UserId, userId.ToString()),
            new(SyncStreamClaims.Privilege, ((int)privileges).ToString()),
            new(SyncStreamClaims.Approved, approved.ToString()),
            new(ClaimTypes.Name, "someone")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public void No_principal_is_a_guest()
    {
        var caller = CallerIdentity.From(null, "conn-1");

        Assert.True(caller.IsGuest);
        Assert.Null(caller.UserId);
        Assert.Null(caller.AccountName);
        Assert.False(caller.HasAtLeast(UserPrivileges.Approved));
    }

    [Fact]
    public void An_unauthenticated_principal_is_a_guest()
    {
        // An empty ClaimsIdentity reports IsAuthenticated false, which must not be mistaken for an account.
        var caller = CallerIdentity.From(new ClaimsPrincipal(new ClaimsIdentity()), "conn-1");

        Assert.True(caller.IsGuest);
    }

    [Fact]
    public void An_authenticated_principal_carries_its_privileges()
    {
        var caller = CallerIdentity.From(Principal(UserPrivileges.Administrator, true), "conn-1");

        Assert.False(caller.IsGuest);
        Assert.Equal(7, caller.UserId);
        Assert.Equal("someone", caller.AccountName);
        Assert.True(caller.HasAtLeast(UserPrivileges.Approved));
        Assert.True(caller.HasAtLeast(UserPrivileges.Administrator));
        Assert.False(caller.HasAtLeast(UserPrivileges.Elevated));
    }

    [Fact]
    public void An_unapproved_account_holds_no_privileges_whatever_its_level_says()
    {
        // Approval is the gate an admin controls, so it has to override the stored level rather than sit
        // beside it. An account can be demoted by revoking approval alone.
        var caller = CallerIdentity.From(Principal(UserPrivileges.Elevated, false), "conn-1");

        Assert.False(caller.IsGuest);
        Assert.False(caller.HasAtLeast(UserPrivileges.Approved));
        Assert.False(caller.HasAtLeast(UserPrivileges.Elevated));
    }

    [Fact]
    public void A_guest_key_is_per_connection_and_an_account_key_is_not()
    {
        var guestA = CallerIdentity.Guest("conn-1");
        var guestB = CallerIdentity.Guest("conn-2");
        Assert.NotEqual(guestA.Key, guestB.Key);

        var accountOnFirstConnection = CallerIdentity.From(Principal(UserPrivileges.Approved, true), "conn-1");
        var accountOnSecondConnection = CallerIdentity.From(Principal(UserPrivileges.Approved, true), "conn-2");
        Assert.Equal(accountOnFirstConnection.Key, accountOnSecondConnection.Key);
    }

    [Fact]
    public void A_principal_with_no_user_id_claim_is_a_guest()
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "someone") };
        var caller = CallerIdentity.From(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), "conn-1");

        Assert.True(caller.IsGuest);
    }
}
