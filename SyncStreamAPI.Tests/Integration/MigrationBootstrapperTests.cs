using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SyncStreamAPI.DataContext;
using Xunit;

namespace SyncStreamAPI.Tests.Integration;

/// <summary>
///     Exercises the startup migration path against a real PostgreSQL server.
/// </summary>
/// <remarks>
///     Skipped unless <c>SYNCSTREAM_TEST_DB</c> holds a connection string for a server the test may create
///     and drop throwaway databases on. Point it at a local server, never at anything that matters:
///     <code>
///     $env:SYNCSTREAM_TEST_DB = 'Host=127.0.0.1;Database=postgres;Username=postgres;Password=...'
///     dotnet test
///     </code>
///     <para>
///         The case that matters is the second one. The schema used to be created by <c>EnsureCreated</c>,
///         which records nothing in <c>__EFMigrationsHistory</c>, so a database that predates migrations has
///         the tables but no history. Calling <c>Migrate()</c> on it fails on the first <c>CREATE TABLE</c>.
///         That is the exact shape of the live database, so it is the thing worth proving rather than
///         assuming.
///     </para>
/// </remarks>
public class MigrationBootstrapperTests
{
    private static string? ServerConnectionString => Environment.GetEnvironmentVariable("SYNCSTREAM_TEST_DB");

    private static bool Enabled => !string.IsNullOrWhiteSpace(ServerConnectionString);

    private static string ConnectionStringFor(string database)
    {
        return new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = database }.ToString();
    }

    private static PostgresContext ContextFor(string database)
    {
        var options = new DbContextOptionsBuilder<PostgresContext>()
            .UseNpgsql(ConnectionStringFor(database))
            .Options;
        return new PostgresContext(options);
    }

    private static void Execute(string database, string sql)
    {
        using var connection = new NpgsqlConnection(ConnectionStringFor(database));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void DropDatabase(string name)
    {
        NpgsqlConnection.ClearAllPools();
        Execute("postgres", $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);");
    }

    /// <summary>A name that cannot collide with anything real, and says what it is if one ever leaks.</summary>
    private static string ThrowawayName()
    {
        return $"syncstream_migrationtest_{Guid.NewGuid():N}"[..40];
    }

    [SkippableFact]
    public void A_fresh_database_gets_the_full_schema_and_records_the_baseline()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_TEST_DB to run migration integration tests.");

        var database = ThrowawayName();
        try
        {
            using var context = ContextFor(database);
            MigrationBootstrapper.Run(context, NullLogger<PostgresContext>.Instance);

            var applied = context.Database.GetAppliedMigrations().ToList();
            var expected = context.Database.GetMigrations().ToList();

            Assert.Equal(expected, applied);
            Assert.Empty(context.Database.GetPendingMigrations());

            // The schema is really there, not just recorded.
            Assert.False(context.Users.Any());
        }
        finally
        {
            DropDatabase(database);
        }
    }

    [SkippableFact]
    public void A_database_built_by_EnsureCreated_adopts_the_baseline_instead_of_replaying_it()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_TEST_DB to run migration integration tests.");

        var database = ThrowawayName();
        try
        {
            // Reproduce the live database: schema present, no migration history at all.
            using (var seed = ContextFor(database))
            {
                seed.Database.EnsureCreated();
                Assert.Empty(seed.Database.GetAppliedMigrations());
            }

            using var context = ContextFor(database);
            MigrationBootstrapper.Run(context, NullLogger<PostgresContext>.Instance);

            var applied = context.Database.GetAppliedMigrations().ToList();
            var baseline = context.Database.GetMigrations().First();

            Assert.Contains(baseline, applied);
            Assert.Empty(context.Database.GetPendingMigrations());
        }
        finally
        {
            DropDatabase(database);
        }
    }

    [SkippableFact]
    public void Running_twice_is_a_no_op_the_second_time()
    {
        Skip.IfNot(Enabled, "Set SYNCSTREAM_TEST_DB to run migration integration tests.");

        var database = ThrowawayName();
        try
        {
            using (var seed = ContextFor(database))
            {
                seed.Database.EnsureCreated();
            }

            using var first = ContextFor(database);
            MigrationBootstrapper.Run(first, NullLogger<PostgresContext>.Instance);

            using var second = ContextFor(database);
            MigrationBootstrapper.Run(second, NullLogger<PostgresContext>.Instance);

            Assert.Empty(second.Database.GetPendingMigrations());
        }
        finally
        {
            DropDatabase(database);
        }
    }
}
