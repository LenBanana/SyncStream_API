using System;
using System.Runtime.CompilerServices;

namespace SyncStreamAPI.DataContext;

internal static class NpgsqlCompatibility
{
    /// <summary>
    ///     Keeps Npgsql's pre-6.0 mapping of <see cref="DateTime" /> to <c>timestamp without time zone</c>.
    /// </summary>
    /// <remarks>
    ///     Npgsql 6 changed the default to <c>timestamp with time zone</c>. The database was created under
    ///     Npgsql 5, so its columns are <c>timestamp</c>, and <c>DbConversation</c> and <c>DbMessage</c>
    ///     still write <c>DateTime.Now</c> (Kind=Local), which a timestamptz column rejects outright.
    ///     <para>
    ///         This is a module initialiser rather than a line in <c>Program.Main</c> because the model also
    ///         gets built without going through Main: the EF design-time tooling and the test project both
    ///         load this assembly directly. Setting it in Main produced a baseline migration describing
    ///         timestamptz columns that the real database does not have.
    ///     </para>
    ///     <para>
    ///         Temporary. Removing it means converting the columns to timestamptz and converting existing
    ///         values with the correct source zone per column, because files and remember tokens were written
    ///         as UTC while conversations and messages were written in the server's local time.
    ///     </para>
    /// </remarks>
    [ModuleInitializer]
    internal static void Apply()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }
}
