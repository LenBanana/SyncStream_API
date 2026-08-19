using System;
using System.Linq;
using SyncStreamAPI.Models;

namespace SyncStreamAPI.Auth;

/// <summary>What should happen when somebody asks to join a room.</summary>
public enum AdmissionOutcome
{
    /// <summary>The supplied room password did not match.</summary>
    WrongPassword,

    /// <summary>This identity is barred from the room.</summary>
    Banned,

    /// <summary>The chosen display name is malformed or reserved by an account.</summary>
    NameNotAllowed,

    /// <summary>Already in the room on this very connection, so there is nothing to do.</summary>
    AlreadyPresent,

    /// <summary>An account reconnecting to a seat it already holds.</summary>
    Reclaimed,

    /// <summary>A new member should be added.</summary>
    Admitted
}

/// <summary>
///     The outcome of an admission check, and what the caller needs to act on it.
/// </summary>
public sealed class AdmissionResult
{
    private AdmissionResult(AdmissionOutcome outcome, string? resolvedName = null, Member? existing = null)
    {
        Outcome = outcome;
        ResolvedName = resolvedName;
        Existing = existing;
    }

    public AdmissionOutcome Outcome { get; }

    /// <summary>The name to join under. Set only for <see cref="AdmissionOutcome.Admitted" />.</summary>
    public string? ResolvedName { get; }

    /// <summary>The seat being reclaimed. Set only for <see cref="AdmissionOutcome.Reclaimed" />.</summary>
    public Member? Existing { get; }

    internal static AdmissionResult Refused(AdmissionOutcome outcome)
    {
        return new AdmissionResult(outcome);
    }

    internal static AdmissionResult Admit(string resolvedName)
    {
        return new AdmissionResult(AdmissionOutcome.Admitted, resolvedName);
    }

    internal static AdmissionResult Reclaim(Member existing)
    {
        return new AdmissionResult(AdmissionOutcome.Reclaimed, existing.username, existing);
    }
}

/// <summary>
///     Decides whether a caller may join a room, and under what name.
/// </summary>
/// <remarks>
///     Extracted from the hub so it can be tested. Every join defect so far has been in this decision rather
///     than in the messaging around it: a guest refused because the validator rejected the name the app
///     itself generates, and a reconnecting member silently dropped because a fallback path supplied an empty
///     password. Neither was reachable by a test while the logic sat inside a hub method that needs the whole
///     application graph to construct.
/// </remarks>
public static class RoomAdmission
{
    /// <summary>
    ///     Works out what should happen, without changing anything.
    /// </summary>
    /// <param name="server">The room's live state.</param>
    /// <param name="caller">Who is asking, taken from the connection.</param>
    /// <param name="connectionId">The connection asking.</param>
    /// <param name="proposedName">The name a guest would like. Ignored for accounts.</param>
    /// <param name="suppliedPassword">The room password as typed.</param>
    /// <param name="roomPassword">The room's password, or null or empty when it is open.</param>
    /// <param name="isRegisteredName">Whether a name belongs to a registered account.</param>
    public static AdmissionResult Evaluate(
        Server server,
        CallerIdentity caller,
        string? connectionId,
        string? proposedName,
        string? suppliedPassword,
        string? roomPassword,
        Func<string, bool> isRegisteredName)
    {
        // Always answered, even for an empty password. The old code stayed silent in that one case, which is
        // how a reconnect into a password room turned into a client waiting forever for a reply.
        //
        // Compared through the password hash rather than by string equality, so the stored value is not
        // something a reader of the table can present back, and so the comparison does not leak where two
        // values first differ. Rooms created before this was true hold a wrapped digest, which verifies the
        // same way.
        if (!string.IsNullOrEmpty(roomPassword)
            && PasswordHashing.Verify(roomPassword, suppliedPassword) == PasswordVerdict.NoMatch)
            return AdmissionResult.Refused(AdmissionOutcome.WrongPassword);

        if (server.IsBanned(caller.Key))
            return AdmissionResult.Refused(AdmissionOutcome.Banned);

        if (server.FindByConnection(connectionId) != null)
            return AdmissionResult.Refused(AdmissionOutcome.AlreadyPresent);

        // Only an account can reclaim: a guest's identity is per connection, so there is nothing durable to
        // match against and a returning guest is simply somebody new.
        if (!caller.IsGuest)
        {
            var existing = server.FindByIdentity(caller.Key);
            if (existing != null) return AdmissionResult.Reclaim(existing);
        }

        var resolvedName = DisplayNames.Resolve(
            caller,
            proposedName,
            server.members.Select(member => member?.username),
            isRegisteredName);

        return resolvedName == null
            ? AdmissionResult.Refused(AdmissionOutcome.NameNotAllowed)
            : AdmissionResult.Admit(resolvedName);
    }
}
