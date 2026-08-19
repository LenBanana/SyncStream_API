using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using Xunit;

namespace SyncStreamAPI.Tests.Contract;

/// <summary>
///     Checks that the Angular client and the hub still agree.
/// </summary>
/// <remarks>
///     Every hub call is keyed by a string on both sides, so neither compiler can see this contract. The C#
///     build proves the server is self-consistent and <c>ng build</c> proves the client is, and nothing
///     proved they matched. Three separate defects came from that gap: an invoke for a method that had never
///     existed, a server callback whose last listener was removed, and argument counts that drifted when a
///     parameter was dropped from a hub method.
///     <para>
///         Skipped unless <c>SYNCSTREAM_FRONTEND</c> points at the Angular project, which is a separate
///         repository. The verify script sets it.
///     </para>
/// </remarks>
public class ClientContractTests
{
    /// <summary>
    ///     Callbacks the server sends that no browser client is expected to listen for, because another kind
    ///     of client consumes them. Anything not listed here with no listener is a dead send.
    /// </summary>
    private static readonly HashSet<string> CallbacksWithoutBrowserListeners = new(StringComparer.Ordinal)
    {
        // Consumed by the chat bot, which is not this front end.
        "sendBotChannelUpdate",
        "sendBotConfirmAuthenticate",

        // Private messaging exists server-side only: this front end neither sends nor receives it.
        // Either wire it up or delete the feature; until then the send goes nowhere.
        "PrivateMessage"
    };

    /// <summary>Hub methods no browser client calls, because another kind of client does.</summary>
    private static readonly HashSet<string> MethodsWithoutBrowserCallers = new(StringComparer.Ordinal)
    {
        "AuthenticateBot",

        // Reachable by anyone who opens a connection while serving no feature in this client. Pinned so
        // the set can only shrink: each is either a feature to wire up or surface to delete, and a new
        // uncalled method fails this test rather than joining the pile.
        "GetAllDownloads",
        "PrivateChat",
        "ReloadDownloadConfig",
        "SendPrivateMessage",
        "WhiteBoardRedo"
    };

    private static string? FrontendPath => Environment.GetEnvironmentVariable("SYNCSTREAM_FRONTEND");

    private static bool Enabled => !string.IsNullOrWhiteSpace(FrontendPath)
                                   && Directory.Exists(Path.Combine(FrontendPath!, "src", "app"));

    private static IEnumerable<string> ClientSources()
    {
        return Directory
            .EnumerateFiles(Path.Combine(FrontendPath!, "src", "app"), "*.ts", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText);
    }

    // invoke('Name', ...) and invoke<T>('Name', ...)
    private static readonly Regex InvokeCall =
        new(@"invoke(?:<[^>]*>)?\(\s*'(?<name>[A-Za-z]+)'", RegexOptions.Compiled);

    // on('name', ...)
    private static readonly Regex OnHandler =
        new(@"\.on\(\s*'(?<name>[A-Za-z]+)'", RegexOptions.Compiled);

    private static Dictionary<string, MethodInfo> HubMethods()
    {
        return typeof(ServerHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Where(method => method.Name is not ("OnConnectedAsync" or "OnDisconnectedAsync"))
            .GroupBy(method => method.Name)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    private static HashSet<string> ServerCallbacks()
    {
        return typeof(IServerHub).GetMethods()
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> ClientInvokes()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in ClientSources())
        foreach (Match match in InvokeCall.Matches(source))
            names.Add(match.Groups["name"].Value);
        return names;
    }

    private static HashSet<string> ClientHandlers()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in ClientSources())
        foreach (Match match in OnHandler.Matches(source))
            names.Add(match.Groups["name"].Value);
        return names;
    }

    [SkippableFact]
    public void Every_method_the_client_invokes_exists_on_the_hub()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_FRONTEND to the Angular project to run client contract tests.");

        var methods = HubMethods();
        var missing = ClientInvokes().Where(name => !methods.ContainsKey(name)).OrderBy(n => n).ToList();

        Assert.True(missing.Count == 0,
            "The client invokes hub methods that do not exist, so every call is rejected at runtime: "
            + string.Join(", ", missing));
    }

    [SkippableFact]
    public void Every_callback_the_client_listens_for_is_actually_sent()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_FRONTEND to the Angular project to run client contract tests.");

        var callbacks = ServerCallbacks();

        // Only consider names that look like hub callbacks: `.on(` is also used for DOM and player events.
        var orphaned = ClientHandlers()
            .Where(name => !callbacks.Contains(name))
            .Where(name => name == name.ToLowerInvariant() && name.Length > 3)
            .Where(name => !KnownNonHubEvents.Contains(name))
            .OrderBy(n => n)
            .ToList();

        Assert.True(orphaned.Count == 0,
            "The client listens for callbacks the server never sends, so those handlers are dead: "
            + string.Join(", ", orphaned));
    }

    [SkippableFact]
    public void Every_callback_the_server_sends_has_a_listener()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_FRONTEND to the Angular project to run client contract tests.");

        // This is the direction that caught the API key bug: the server answered with `userlogin` long
        // after the last listener for it had been removed, so the key was created and never displayed.
        var handlers = ClientHandlers();
        var unheard = ServerCallbacks()
            .Where(name => !handlers.Contains(name))
            .Where(name => !CallbacksWithoutBrowserListeners.Contains(name))
            .OrderBy(n => n)
            .ToList();

        Assert.True(unheard.Count == 0,
            "The server sends callbacks nothing listens for, so the result is silently discarded: "
            + string.Join(", ", unheard));
    }

    [SkippableFact]
    public void Every_hub_method_has_a_caller()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_FRONTEND to the Angular project to run client contract tests.");

        var invokes = ClientInvokes();
        var uncalled = HubMethods().Keys
            .Where(name => !invokes.Contains(name))
            .Where(name => !MethodsWithoutBrowserCallers.Contains(name))
            .OrderBy(n => n)
            .ToList();

        // An uncalled hub method is reachable by anyone who opens a connection while serving no feature,
        // which is exactly what ChangeRoom and SendServer were.
        Assert.True(uncalled.Count == 0,
            "Hub methods nothing calls, which are unused attack surface: " + string.Join(", ", uncalled));
    }

    /// <summary>
    ///     Lowercase `.on(` names that belong to players and DOM elements rather than the hub.
    /// </summary>
    private static readonly HashSet<string> KnownNonHubEvents = new(StringComparer.Ordinal)
    {
        "play", "pause", "ended", "ready", "timeupdate", "volumechange", "canplay", "waiting", "seeking",
        "seeked", "error", "loadedmetadata", "loadeddata", "progress", "stalled", "suspend", "abort",
        "emptied", "durationchange", "ratechange", "playing", "click", "change", "input", "keydown",
        "keyup", "mousemove", "mouseleave", "mouseenter", "resize", "scroll", "load", "message", "close",
        "open", "statechange", "enterfullscreen", "exitfullscreen", "captionsenabled", "captionsdisabled",
        "languagechange", "qualitychange", "controlshidden", "controlsshown", "fullscreenchange",
        // mediasoup transport and producer events.
        "connect", "produce", "transportclose", "trackended", "icegatheringstatechange"
    };
}
