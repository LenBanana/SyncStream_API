using System;
using System.Collections.Generic;
using System.Security.Claims;
using SyncStreamAPI.Auth;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using Xunit;

namespace SyncStreamAPI.Tests.Rooms;

/// <summary>
///     Joining a room, with particular attention to doing it without an account.
/// </summary>
/// <remarks>
///     Anonymous use is a core path, and it is where every join defect so far has landed: a guest refused
///     because the validator rejected the very name the client generates, and a client left waiting forever
///     because one refusal branch answered with silence. Both were unreachable by a test while this decision
///     lived inside a hub method that needs the whole application graph to construct.
///     <para>
///         Inputs here are the shapes the client really sends, not invented ones. The earlier guest test
///         passed while guests were completely broken because it made up a tidy name of its own.
///     </para>
/// </remarks>
public class GuestAdmissionTests
{
    /// <summary>The shape the client builds: 'Anon' + '#' + a random number.</summary>
    private const string GeneratedGuestName = "Anon#4821";

    private static bool NothingRegistered(string name)
    {
        return false;
    }

    private static CallerIdentity Guest(string connectionId = "conn-guest")
    {
        return CallerIdentity.Guest(connectionId);
    }

    private static CallerIdentity Account(string username, int userId = 7,
        UserPrivileges privileges = UserPrivileges.Approved)
    {
        var claims = new List<Claim>
        {
            new(SyncStreamClaims.UserId, userId.ToString()),
            new(SyncStreamClaims.Privilege, ((int)privileges).ToString()),
            new(SyncStreamClaims.Approved, "True"),
            new(ClaimTypes.Name, username)
        };
        return CallerIdentity.From(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), $"conn-{username}");
    }

    private static AdmissionResult Evaluate(
        Server server,
        CallerIdentity caller,
        string? proposedName = GeneratedGuestName,
        string? supplied = null,
        string? roomPassword = null,
        Func<string, bool>? isRegistered = null)
    {
        return RoomAdmission.Evaluate(server, caller, caller.ConnectionId, proposedName, supplied, roomPassword,
            isRegistered ?? NothingRegistered);
    }

    [Fact]
    public void A_guest_can_join_an_open_room_under_the_name_the_client_generates()
    {
        var result = Evaluate(new Server(), Guest());

        Assert.Equal(AdmissionOutcome.Admitted, result.Outcome);
        Assert.Equal(GeneratedGuestName + " (guest)", result.ResolvedName);
    }

    /// <summary>A room password as it is now stored: hashed, never the value the client sends.</summary>
    private static string Stored(string password)
    {
        return PasswordHashing.Hash(password);
    }

    [Fact]
    public void A_guest_can_join_a_password_room_with_the_right_password()
    {
        var result = Evaluate(new Server(), Guest(), supplied: "the room password",
            roomPassword: Stored("the room password"));

        Assert.Equal(AdmissionOutcome.Admitted, result.Outcome);
    }

    [Fact]
    public void The_stored_room_password_cannot_be_presented_back_to_join()
    {
        // What the room holds is no longer what a joiner sends, so reading it out of the database or off the
        // wire does not get anybody into the room.
        var stored = Stored("the room password");

        var result = Evaluate(new Server(), Guest(), supplied: stored, roomPassword: stored);

        Assert.Equal(AdmissionOutcome.WrongPassword, result.Outcome);
    }

    [Fact]
    public void A_room_password_stored_in_the_old_form_still_admits_the_right_password()
    {
        // Rooms created before passwords were hashed hold the digest the browser used to send, wrapped at
        // startup. Joining one has to keep working without the room being recreated.
        var wrapped = PasswordHashing.WrapLegacyDigest(
            System.Convert.ToHexString(
                System.Security.Cryptography.SHA512.HashData(
                    System.Text.Encoding.UTF8.GetBytes("the room password"))).ToLowerInvariant());

        var result = Evaluate(new Server(), Guest(), supplied: "the room password", roomPassword: wrapped);

        Assert.Equal(AdmissionOutcome.Admitted, result.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("the wrong password")]
    public void A_wrong_or_missing_password_is_always_answered(string? supplied)
    {
        // Including the empty string. That case used to fall through without replying at all, so a client
        // that supplied one waited forever for an answer that never came.
        var result = Evaluate(new Server(), Guest(), supplied: supplied,
            roomPassword: Stored("the room password"));

        Assert.Equal(AdmissionOutcome.WrongPassword, result.Outcome);
    }

    [Fact]
    public void A_guest_cannot_take_a_registered_accounts_name()
    {
        var result = Evaluate(new Server(), Guest(), proposedName: "admin", isRegistered: name => name == "admin");

        Assert.Equal(AdmissionOutcome.NameNotAllowed, result.Outcome);
    }

    [Fact]
    public void Two_guests_choosing_the_same_name_are_both_let_in()
    {
        var server = new Server();
        server.Join(new Member
        {
            Identity = "guest:conn-first",
            username = GeneratedGuestName + " (guest)",
            ConnectionId = "conn-first"
        });

        var result = Evaluate(server, Guest("conn-second"));

        Assert.Equal(AdmissionOutcome.Admitted, result.Outcome);
        Assert.Equal(GeneratedGuestName + " 2 (guest)", result.ResolvedName);
    }

    [Fact]
    public void A_banned_identity_is_refused_before_any_name_is_considered()
    {
        var server = new Server();
        var guest = Guest();
        server.BannedIdentities.Add(guest.Key);

        var result = Evaluate(server, guest, proposedName: "!!!invalid!!!");

        Assert.Equal(AdmissionOutcome.Banned, result.Outcome);
    }

    [Fact]
    public void Joining_twice_on_one_connection_is_not_an_error()
    {
        var server = new Server();
        var guest = Guest();
        server.Join(new Member
        {
            Identity = guest.Key,
            username = GeneratedGuestName + " (guest)",
            ConnectionId = guest.ConnectionId
        });

        Assert.Equal(AdmissionOutcome.AlreadyPresent, Evaluate(server, guest).Outcome);
    }

    [Fact]
    public void A_guest_returning_on_a_new_connection_is_somebody_new()
    {
        // A guest identity is per connection, so there is nothing durable to reclaim. This is deliberate:
        // reclaiming on anything a client controls would let one guest take another's seat.
        var server = new Server();
        server.Join(new Member
        {
            Identity = "guest:conn-old",
            username = GeneratedGuestName + " (guest)",
            ConnectionId = "conn-old"
        });

        var result = Evaluate(server, Guest("conn-new"));

        Assert.Equal(AdmissionOutcome.Admitted, result.Outcome);
        Assert.NotEqual(GeneratedGuestName + " (guest)", result.ResolvedName);
    }

    [Fact]
    public void An_account_reconnecting_reclaims_its_seat_rather_than_joining_twice()
    {
        var server = new Server();
        var account = Account("someone");
        var seat = new Member
        {
            Identity = account.Key,
            username = "someone",
            ConnectionId = "conn-old",
            ishost = true
        };
        server.Join(seat);

        var result = RoomAdmission.Evaluate(server, account, "conn-new", null, null, null, NothingRegistered);

        Assert.Equal(AdmissionOutcome.Reclaimed, result.Outcome);
        Assert.Same(seat, result.Existing);
        Assert.True(result.Existing!.ishost);
    }

    [Fact]
    public void An_account_always_appears_under_its_own_name()
    {
        var result = Evaluate(new Server(), Account("realuser"), proposedName: "someoneelse");

        Assert.Equal(AdmissionOutcome.Admitted, result.Outcome);
        Assert.Equal("realuser", result.ResolvedName);
    }

    [Fact]
    public void A_ban_on_an_account_survives_a_reconnect()
    {
        var server = new Server();
        var account = Account("banned");
        server.BannedIdentities.Add(account.Key);

        // Same account, different connection: a ban keyed to a connection id would have let this through.
        var result = RoomAdmission.Evaluate(server, account, "conn-fresh", null, null, null, NothingRegistered);

        Assert.Equal(AdmissionOutcome.Banned, result.Outcome);
    }
}
