using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.Hubs;
using Xunit;

namespace SyncStreamAPI.Tests.Auth;

/// <summary>
///     Pins which hub methods a caller with no account can invoke.
/// </summary>
/// <remarks>
///     A public method on a hub is callable by anyone who can open a connection, which is easy to forget when
///     adding one. Two methods reached production that way: <c>ChangeRoom</c> let any anonymous caller rename
///     any room and strip its password, and <c>SendServer</c> let one broadcast a fabricated server state into
///     any room. Both had no caller in the client at all, so nothing else would have found them.
///     <para>
///         Anonymous does not mean unguarded. Guests are allowed to watch, chat and drive playback, so these
///         methods are open on purpose and the ones that change a room check <c>CheckPrivileges</c> internally,
///         which requires being the host or holding privileges when the room is a privileged one.
///     </para>
/// </remarks>
public class HubAuthorizationTests
{
    /// <summary>
    ///     Every hub method reachable without an account. Pinned exactly: adding a method without an
    ///     <see cref="AuthorizeAttribute" /> fails this test, and so does removing one from the list, so it
    ///     cannot drift from reality in either direction.
    /// </summary>
    private static readonly string[] IntentionallyAnonymous =
    {
        // Connection lifecycle, not client-invokable.
        "OnConnectedAsync",
        "OnDisconnectedAsync",

        // Joining, presence and leaving. A guest has to be able to get into a room.
        "AddUser",
        "RemoveUser",
        "GetRooms",
        "Ping",

        // Room moderation, gated in-method by CheckPrivileges rather than by a policy, because in an
        // unprivileged room any member is allowed to drive.
        "AddVideo",
        "RemoveVideo",
        "MoveVideo",
        "NextVideo",
        "PlayVideo",
        "PlayPause",
        "SetTime",
        "ChangeHost",
        "BanUser",
        "ClearChat",

        // Chat.
        "SendMessage",
        "SendPrivateMessage",
        "GetMessages",

        // Games. Playable by guests.
        "PlayChess",
        "MoveChessPiece",
        "ResetChess",
        "EndChess",
        "MakeAi",
        "PlayBlackjack",
        "SpectateBlackjack",
        "AddBlackjackAi",
        "SetBet",
        "TakePull",
        "TakeSplitPull",
        "PlayGallows",
        "NewGallow",

        // Whiteboard.
        "WhiteBoardJoin",
        "WhiteBoardUpdate",
        "WhiteBoardClear",
        "WhiteBoardUndo",
        "WhiteBoardRedo",

        // Watching a broadcast, file share or live stream through the SFU. Guests may watch, so these are
        // gated in-method on membership of the room the SFU room belongs to, and on owning the transport.
        // Sending is refused to a guest in CreateWebRtcTransport; Produce itself carries the policy.
        "GetRouterRtpCapabilities",
        "JoinSfuRoom",
        "LeaveSfuRoom",
        "CreateWebRtcTransport",
        "ConnectTransport",
        "RestartSfuIce",
        "Consume"
    };

    [Fact]
    public void Only_the_expected_hub_methods_are_callable_without_an_account()
    {
        var anonymous = typeof(ServerHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Where(method => !method.GetCustomAttributes<AuthorizeAttribute>(true).Any())
            .Select(method => method.Name)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = IntentionallyAnonymous.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, anonymous);
    }

    [Fact]
    public void Every_guarded_hub_method_names_a_policy()
    {
        // A bare [Authorize] would only require "signed in", which for this hub is nearly meaningless: an
        // unapproved account authenticates too. Requiring a policy keeps privilege explicit.
        var withoutPolicy = typeof(ServerHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>(true)
                .Select(attribute => new { method.Name, attribute.Policy }))
            .Where(entry => string.IsNullOrEmpty(entry.Policy))
            .Select(entry => entry.Name)
            .ToList();

        Assert.True(withoutPolicy.Count == 0,
            "Hub methods with [Authorize] but no policy: " + string.Join(", ", withoutPolicy));
    }

    [Fact]
    public void No_hub_method_still_takes_a_credential_as_an_argument()
    {
        var offenders = (from method in typeof(ServerHub)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            where !method.IsSpecialName
            from parameter in method.GetParameters()
            where parameter.Name is "token" or "apiKey" or "password" && parameter.ParameterType == typeof(string)
            where !(method.Name == "AddUser" && parameter.Name == "password")
            select $"{method.Name}({parameter.Name})").ToList();

        Assert.True(offenders.Count == 0,
            "Identity comes from the connection, so these arguments should be gone: "
            + string.Join(", ", offenders));
    }
}
