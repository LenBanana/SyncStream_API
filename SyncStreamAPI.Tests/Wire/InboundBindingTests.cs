using System;
using System.Linq;
using System.Reflection;
using SyncStreamAPI.Wire.Auth;
using SyncStreamAPI.Wire.Rooms;
using Xunit;

namespace SyncStreamAPI.Tests.Wire;

/// <summary>
///     Guards what a client is allowed to <em>set</em>.
/// </summary>
/// <remarks>
///     Registration used to bind a <c>DbUser</c> straight from the wire and save it, so a client could
///     register itself with <c>approved = 1</c> and <c>userprivileges = Elevated</c> and become an
///     administrator in a single call. Room creation had the same shape: binding the domain room also bound
///     its live server state, letting a caller seed a new room with a fabricated member list, chat history
///     and playlist. These tests assert the inbound types cannot express any of that.
/// </remarks>
public class InboundBindingTests
{
    /// <summary>Fields whose value is the server's to decide, never the caller's.</summary>
    private static readonly string[] ServerOwnedFieldNames =
    {
        "approved",
        "userprivileges",
        "StreamToken",
        "ApiKey",
        "usersalt",
        "RememberTokens",
        "ID",
        "server",
        "isPrivileged"
    };

    public static TheoryData<Type> InboundTypes => new()
    {
        typeof(CredentialsDto),
        typeof(CreateRoomRequestDto)
    };

    [Theory]
    [MemberData(nameof(InboundTypes))]
    public void Inbound_types_cannot_set_server_owned_fields(Type inbound)
    {
        var settable = inbound.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .Select(property => property.Name)
            .Where(name => ServerOwnedFieldNames.Contains(name))
            .ToList();

        Assert.Empty(settable);
    }

    [Theory]
    [MemberData(nameof(InboundTypes))]
    public void Inbound_types_do_not_inherit_from_a_persistence_entity(Type inbound)
    {
        // UserDTO inherited DbUser, which is how it ended up carrying credentials it had no reason to hold.
        var baseType = inbound.BaseType;

        Assert.True(
            baseType == typeof(object),
            $"{inbound.Name} inherits {baseType?.Name}; inbound wire types should be flat.");
    }

    [Fact]
    public void Credentials_carry_only_a_username_and_a_password()
    {
        var properties = typeof(CredentialsDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "password", "username" }, properties);
    }

    [Fact]
    public void Change_user_request_cannot_change_privileges()
    {
        var properties = typeof(ChangeUserRequestDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain("approved", properties);
        Assert.DoesNotContain("userprivileges", properties);
    }
}
