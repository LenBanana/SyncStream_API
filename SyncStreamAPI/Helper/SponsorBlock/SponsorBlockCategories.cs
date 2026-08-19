using System;
using System.Collections.Generic;
using System.Linq;

namespace SyncStreamAPI.Helper.SponsorBlock;

/// <summary>
///     The SponsorBlock categories, and which of them are skipped unless an administrator says otherwise.
/// </summary>
public static class SponsorBlockCategories
{
    /// <summary>Every category the upstream API serves.</summary>
    public static readonly string[] All =
    {
        "sponsor",
        "selfpromo",
        "interaction",
        "intro",
        "outro",
        "preview",
        "filler",
        "music_offtopic",
        "hook",
        "exclusive_access"
    };

    /// <summary>
    ///     What is skipped on a fresh install.
    /// </summary>
    /// <remarks>
    ///     Two categories, where the client used to default to every one of them except filler. That skipped
    ///     intros, outros, greetings, recaps and offtopic music by default, which is far more than SponsorBlock
    ///     itself does: upstream auto-skips sponsor only and leaves the rest to be shown or asked about. Skipping
    ///     an intro or a greeting removes something people often want to watch, and in a room nobody but the host
    ///     could opt out of it.
    /// </remarks>
    public static readonly string[] Default = { "sponsor", "selfpromo" };

    /// <summary>Keeps only the categories that exist, so an unknown value cannot be stored or served.</summary>
    public static List<string> Sanitise(IEnumerable<string>? categories)
    {
        if (categories == null) return Default.ToList();

        return categories
            .Where(category => category != null)
            .Select(category => category.Trim().ToLowerInvariant())
            .Where(category => All.Contains(category))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
