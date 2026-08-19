using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SyncStreamAPI.Auth;

/// <summary>
///     Decides what a caller is called in a room.
/// </summary>
/// <remarks>
///     Guests may choose a name, which is why this exists. An account's name is never negotiable: it comes
///     from the account, so a signed-in user cannot appear as someone else. A guest's proposed name is
///     validated, refused if it belongs to a registered account, and disambiguated against the room. Without
///     the reservation rule, impersonation walks straight back in through the guest path.
/// </remarks>
public static class DisplayNames
{
    public const int MinLength = 2;
    public const int MaxLength = 20;

    private static readonly Regex Allowed = new(@"^[\w \-.]+$", RegexOptions.Compiled);

    /// <summary>Marks a name as belonging to a guest rather than an account.</summary>
    public const string GuestSuffix = " (guest)";

    public static bool IsWellFormed(string? proposed)
    {
        if (string.IsNullOrWhiteSpace(proposed)) return false;
        var trimmed = proposed.Trim();
        return trimmed.Length >= MinLength && trimmed.Length <= MaxLength && Allowed.IsMatch(trimmed);
    }

    /// <summary>
    ///     Resolves the name to use, given who is calling and who is already in the room.
    /// </summary>
    /// <param name="caller">The connection's identity.</param>
    /// <param name="proposed">The name the client asked for. Ignored for accounts.</param>
    /// <param name="takenInRoom">Names already present in the room.</param>
    /// <param name="isRegisteredName">Whether a name belongs to a registered account.</param>
    /// <returns>The resolved name, or null when a guest's proposal cannot be used.</returns>
    public static string? Resolve(
        CallerIdentity caller,
        string? proposed,
        IEnumerable<string?> takenInRoom,
        Func<string, bool> isRegisteredName)
    {
        // An account is always itself.
        if (!caller.IsGuest) return caller.AccountName;

        if (!IsWellFormed(proposed)) return null;

        var name = proposed!.Trim();

        // Reserved: a guest may not wear a registered account's name.
        if (isRegisteredName(name)) return null;

        var taken = new HashSet<string>(
            takenInRoom.Where(existing => existing != null).Select(existing => existing!),
            StringComparer.OrdinalIgnoreCase);

        var candidate = name + GuestSuffix;
        if (!taken.Contains(candidate)) return candidate;

        // Two guests picking the same name is ordinary, so disambiguate rather than refuse.
        for (var suffix = 2; suffix <= 99; suffix++)
        {
            candidate = $"{name} {suffix}{GuestSuffix}";
            if (!taken.Contains(candidate)) return candidate;
        }

        return null;
    }
}
