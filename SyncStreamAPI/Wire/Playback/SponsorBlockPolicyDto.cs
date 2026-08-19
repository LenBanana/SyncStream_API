using System.Collections.Generic;

namespace SyncStreamAPI.Wire.Playback;

/// <summary>
///     Which SponsorBlock categories the server skips.
/// </summary>
/// <remarks>
///     Read by every client and written by an administrator. Every client needs it because any of them may be the
///     host, and the host is the one that performs a skip.
/// </remarks>
public class SponsorBlockPolicyDto
{
    public SponsorBlockPolicyDto()
    {
    }

    public SponsorBlockPolicyDto(IEnumerable<string> skipCategories, IEnumerable<string> availableCategories)
    {
        this.skipCategories = new List<string>(skipCategories);
        this.availableCategories = new List<string>(availableCategories);
    }

    /// <summary>Categories that are skipped automatically.</summary>
    public List<string> skipCategories { get; set; } = new();

    /// <summary>
    ///     Every category that could be chosen.
    /// </summary>
    /// <remarks>
    ///     Sent with the policy so the settings UI lists what the server actually recognises rather than its own
    ///     copy of the list, which is the sort of pair that drifts.
    /// </remarks>
    public List<string> availableCategories { get; set; } = new();
}
