using System;
using System.Collections.Generic;
using System.Security.Claims;
using SyncStreamAPI.Auth;
using SyncStreamAPI.PostgresModels;
using Xunit;

namespace SyncStreamAPI.Tests.Auth;

/// <summary>
///     The guest naming rules, which are the one place a client still influences how it is identified.
/// </summary>
public class DisplayNamesTests
{
    private static readonly string[] NoOneInRoom = Array.Empty<string>();

    private static CallerIdentity Account(string username, UserPrivileges privileges = UserPrivileges.Approved)
    {
        var claims = new List<Claim>
        {
            new(SyncStreamClaims.UserId, "7"),
            new(SyncStreamClaims.Privilege, ((int)privileges).ToString()),
            new(SyncStreamClaims.Approved, "True"),
            new(ClaimTypes.Name, username)
        };
        return CallerIdentity.From(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), "conn-1");
    }

    private static bool NothingRegistered(string name)
    {
        return false;
    }

    [Fact]
    public void An_account_keeps_its_own_name_whatever_it_asks_for()
    {
        var resolved = DisplayNames.Resolve(Account("realuser"), "someoneelse", NoOneInRoom, NothingRegistered);

        Assert.Equal("realuser", resolved);
    }

    [Fact]
    public void A_guest_is_marked_as_a_guest()
    {
        var resolved = DisplayNames.Resolve(CallerIdentity.Guest("conn-1"), "Visitor", NoOneInRoom, NothingRegistered);

        Assert.Equal("Visitor (guest)", resolved);
    }

    [Fact]
    public void A_guest_cannot_take_a_registered_account_name()
    {
        // Without this the impersonation that removing the username argument closed would come straight back
        // through the guest path.
        var resolved = DisplayNames.Resolve(
            CallerIdentity.Guest("conn-1"),
            "admin",
            NoOneInRoom,
            name => name == "admin");

        Assert.Null(resolved);
    }

    [Fact]
    public void Two_guests_choosing_one_name_are_disambiguated()
    {
        var resolved = DisplayNames.Resolve(
            CallerIdentity.Guest("conn-2"),
            "Visitor",
            new[] { "Visitor (guest)" },
            NothingRegistered);

        Assert.Equal("Visitor 2 (guest)", resolved);
    }

    [Theory]
    [InlineData("Anon#1234")]
    [InlineData("Anon#100")]
    public void The_guest_name_the_client_generates_is_accepted(string generated)
    {
        // The client builds guest names as 'Anon' + '#' + a random number. The validator originally excluded
        // '#', so the app's own name failed its own rules and no guest could join a room at all. Pinned to
        // the shape the client actually produces rather than to what the rules were meant to allow.
        var resolved = DisplayNames.Resolve(
            CallerIdentity.Guest("conn-1"), generated, NoOneInRoom, NothingRegistered);

        Assert.Equal(generated + " (guest)", resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("this name is far too long to accept")]
    [InlineData("bad<name>")]
    [InlineData("semi;colon")]
    public void Malformed_guest_names_are_refused(string? proposed)
    {
        var resolved = DisplayNames.Resolve(CallerIdentity.Guest("conn-1"), proposed, NoOneInRoom, NothingRegistered);

        Assert.Null(resolved);
    }

    [Fact]
    public void Guest_name_matching_is_case_insensitive()
    {
        var resolved = DisplayNames.Resolve(
            CallerIdentity.Guest("conn-2"),
            "visitor",
            new[] { "Visitor (guest)" },
            NothingRegistered);

        Assert.Equal("visitor 2 (guest)", resolved);
    }
}
