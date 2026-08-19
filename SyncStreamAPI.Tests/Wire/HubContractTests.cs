using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Wire.Users;
using Xunit;

namespace SyncStreamAPI.Tests.Wire;

/// <summary>
///     Guards the boundary between what the server keeps and what it sends.
/// </summary>
/// <remarks>
///     These are the tests that keep the Phase 0 fixes fixed. Three separate broadcasts used to carry
///     credentials to every connected client: the user list carried every user's stream token and API key,
///     and the room list carried every room's password (which the join check compared against verbatim,
///     so broadcasting it defeated room passwords entirely) along with every member's connection id and
///     private message log. Each of those was a one-line change away from happening again, so the rule is
///     enforced here rather than remembered.
/// </remarks>
public class HubContractTests
{
    /// <summary>
    ///     Property names that are credentials or credential-equivalent. A leak of any of these is an
    ///     account takeover, not an information disclosure: the stream token authenticates RTMP publishing
    ///     and the API key authenticates endpoints that require Administrator.
    /// </summary>
    private static readonly string[] CredentialPropertyNames =
    {
        "password",
        "usersalt",
        "StreamToken",
        "ApiKey",
        "RememberTokens",
        "Token",
        "PrivateMessages"
    };

    /// <summary>
    ///     The profile a user receives about themselves, carrying their own keys for the settings UI.
    /// </summary>
    private static readonly Type SelfScopedType = typeof(SelfProfileDto);

    /// <summary>
    ///     Types allowed to carry a credential because every send of them is addressed to the one user the
    ///     credential belongs to. Verified by reading the call sites, so adding to this set means checking
    ///     that every send really is caller-scoped.
    /// </summary>
    private static readonly Type[] SelfScopedTypes =
    {
        typeof(SelfProfileDto)
    };

    /// <summary>
    ///     Payload types that live under <c>Models/</c> but are only ever sent to clients, so they are
    ///     effectively wire types in the wrong folder. Relocating them under <c>Wire/</c> is a job for the
    ///     wire-boundary phase; until then they are listed here so the set is pinned.
    /// </summary>
    /// <remarks>
    ///     Pinned exactly, in both directions. A new domain type reaching a client fails the test, and so
    ///     does an entry that no longer reaches one, which is what keeps this list shrinking as types move
    ///     to <c>Wire/</c> instead of quietly rotting.
    /// </remarks>
    private static readonly string[] KnownDomainTypesOnTheWire =
    {
        // Games
        "BlackjackDealer",
        "BlackjackMember",
        "ChessMove",
        "GallowMember",
        "PlayingCard",
        // Room and media
        "ChatMessage",
        "Drawing",
        "DreckVideo",
        "EditorProcess",
        "SelectedShapeOptions",
        // Files and downloads
        "DownloadFileInfo",
        "DownloadInfo",
        "FileStorageInfo",
        // WebRTC, SFU and VoIP signalling
        "SfuNewProducerNotification",
        "SfuRecordingInfo",
        "VoipOffer",
        "VoipParticipantDto",
        "WebRtcClientOffer",
        // Live streaming and bots
        "BotLiveChannelInfo",
        "LiveUserDTO",
        // Cross-cutting
        "Dialog",
        "PrivilegeInfo",
        "ServerHealthDto"
    };

    private static IEnumerable<MethodInfo> HubCallbacks()
    {
        return typeof(IServerHub).GetMethods();
    }

    /// <summary>
    ///     Walks every type reachable from a hub callback's parameters, through collections and
    ///     properties, so a credential nested three levels down is still caught.
    /// </summary>
    private static IEnumerable<Type> ReachableTypes(Type root)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();

            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                foreach (var argument in type.GetGenericArguments())
                    queue.Enqueue(argument);

            if (type.IsArray)
            {
                var element = type.GetElementType();
                if (element != null) queue.Enqueue(element);
                continue;
            }

            if (type.IsPrimitive || type == typeof(string) || type.IsEnum) continue;
            if (type.Namespace == null || !type.Namespace.StartsWith("SyncStreamAPI")) continue;
            if (!seen.Add(type)) continue;

            yield return type;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                queue.Enqueue(property.PropertyType);
        }
    }

    [Fact]
    public void No_credential_reaches_a_client_except_on_the_self_scoped_profile()
    {
        var offenders = new List<string>();

        foreach (var callback in HubCallbacks())
        foreach (var parameter in callback.GetParameters())
        foreach (var type in ReachableTypes(parameter.ParameterType))
        {
            if (SelfScopedTypes.Contains(type)) continue;

            var leaked = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => CredentialPropertyNames.Contains(property.Name))
                .Select(property => property.Name);

            foreach (var property in leaked)
                offenders.Add($"{callback.Name}(...) -> {type.Name}.{property}");
        }

        Assert.True(offenders.Count == 0,
            "Credential reachable from a client callback: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_self_scoped_profile_is_never_sent_as_a_collection()
    {
        // A list of self profiles can only mean a broadcast, which is how the stream tokens and API keys
        // got out in the first place.
        var offenders = (from callback in HubCallbacks()
            from parameter in callback.GetParameters()
            where parameter.ParameterType != SelfScopedType
            from reachable in ReachableTypes(parameter.ParameterType)
            where reachable == SelfScopedType
            select $"{callback.Name}({parameter.ParameterType.Name})").ToList();

        Assert.True(offenders.Count == 0,
            "Self-scoped profile sent inside a larger payload: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Persistence_entities_never_reach_a_client()
    {
        var offenders = (from callback in HubCallbacks()
            from parameter in callback.GetParameters()
            from type in ReachableTypes(parameter.ParameterType)
            where type.Namespace == "SyncStreamAPI.PostgresModels"
            select $"{callback.Name}(...) -> {type.Name}").Distinct().ToList();

        Assert.True(offenders.Count == 0,
            "Persistence entity reachable from a client callback: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_set_of_domain_types_reaching_clients_is_exactly_what_we_expect()
    {
        var onTheWire = (from callback in HubCallbacks()
                from parameter in callback.GetParameters()
                from type in ReachableTypes(parameter.ParameterType)
                where type.Namespace != null && type.Namespace.StartsWith("SyncStreamAPI.Models")
                select type.Name)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = KnownDomainTypesOnTheWire.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, onTheWire);
    }
}
