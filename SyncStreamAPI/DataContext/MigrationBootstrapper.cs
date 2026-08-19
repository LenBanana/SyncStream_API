using System.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SyncStreamAPI.DataContext;

/// <summary>
///     Brings the database up to date at startup, including the one-time adoption of the baseline on a
///     database whose schema predates migrations.
/// </summary>
public static class MigrationBootstrapper
{
    /// <summary>
    ///     A table that has existed for as long as the schema has. Used to tell "empty database" apart from
    ///     "database built by EnsureCreated", which is the distinction the baseline adoption turns on.
    /// </summary>
    private const string SchemaProbe = "public.\"Users\"";

    public static void Run(PostgresContext context, ILogger logger)
    {
        // A database that does not exist yet needs no adoption: Migrate creates it and applies everything,
        // baseline included.
        if (context.Database.CanConnect()) AdoptBaselineIfSchemaPredatesMigrations(context, logger);

        var pending = context.Database.GetPendingMigrations().ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date");
            return;
        }

        logger.LogInformation("Applying {Count} database migration(s): {Migrations}",
            pending.Count, string.Join(", ", pending));
        context.Database.Migrate();
        logger.LogInformation("Database migrations applied");
    }

    /// <summary>
    ///     The schema used to be created by <c>EnsureCreated</c>, which records nothing in
    ///     <c>__EFMigrationsHistory</c>. On such a database the baseline migration describes tables that are
    ///     already there, so running it would fail on the first <c>CREATE TABLE</c>. Recording it as applied
    ///     instead is correct by construction: the baseline was generated from that very schema.
    /// </summary>
    private static void AdoptBaselineIfSchemaPredatesMigrations(PostgresContext context, ILogger logger)
    {
        var baseline = context.Database.GetMigrations().FirstOrDefault();
        if (baseline == null) return;

        // Empty when the history table is absent, which is exactly the EnsureCreated case.
        if (context.Database.GetAppliedMigrations().Any()) return;

        if (!SchemaExists(context))
        {
            logger.LogInformation("Empty database, the baseline migration will be applied normally");
            return;
        }

        logger.LogWarning(
            "Schema exists but no migration history was found, so it predates migrations. " +
            "Recording {Baseline} as already applied rather than re-running it", baseline);

        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed) connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                                      "MigrationId"    character varying(150) NOT NULL,
                                      "ProductVersion" character varying(32)  NOT NULL,
                                      CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                                  );

                                  INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                                  VALUES (@migrationId, @productVersion)
                                  ON CONFLICT ("MigrationId") DO NOTHING;
                                  """;

            var migrationId = command.CreateParameter();
            migrationId.ParameterName = "migrationId";
            migrationId.Value = baseline;
            command.Parameters.Add(migrationId);

            var productVersion = command.CreateParameter();
            productVersion.ParameterName = "productVersion";
            productVersion.Value = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "9.0.0";
            command.Parameters.Add(productVersion);

            command.ExecuteNonQuery();
        }
        finally
        {
            if (wasClosed) connection.Close();
        }

        logger.LogInformation("Baseline {Baseline} recorded as applied", baseline);
    }

    private static bool SchemaExists(PostgresContext context)
    {
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed) connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT to_regclass('{SchemaProbe}') IS NOT NULL;";
            return command.ExecuteScalar() is true;
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }
}
