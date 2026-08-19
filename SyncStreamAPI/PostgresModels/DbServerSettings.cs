using System.Collections.Generic;

namespace SyncStreamAPI.PostgresModels;

/// <summary>
///     Settings that belong to the server rather than to a person or a room.
/// </summary>
/// <remarks>
///     One row, always id 1. There was nowhere to put a setting an administrator decides, so anything that needed
///     to be decided once for everybody was either compiled in or, in the case of sponsor skipping, left in each
///     browser's own settings where it could not do the job.
/// </remarks>
public class DbServerSettings
{
    /// <summary>The only row's id.</summary>
    public const int SingletonId = 1;

    public int ID { get; set; }

    /// <summary>
    ///     SponsorBlock categories that are skipped automatically.
    /// </summary>
    /// <remarks>
    ///     Here rather than in each browser because only the host's client performs a skip: the position it jumps
    ///     to is what the rest of the room follows. So whoever happened to be host decided for everybody, using
    ///     settings they had chosen for themselves, and a viewer's own category choices changed nothing at all.
    ///     A room-wide decision belongs somewhere room-wide.
    ///     <para>
    ///         Stored as a Postgres text array rather than a delimited string, so a category containing a
    ///         separator can never be a problem and the column is readable as what it is.
    ///     </para>
    /// </remarks>
    public List<string> SponsorBlockSkipCategories { get; set; } = new();
}
