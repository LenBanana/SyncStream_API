using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SyncStreamAPI.DataContext;

/// <summary>
///     Lets the EF tooling build a <see cref="PostgresContext" /> without starting the application.
/// </summary>
/// <remarks>
///     Without this, <c>dotnet ef</c> falls back to running the host builder, which runs
///     <c>Startup.ConfigureServices</c>, which constructs <c>MainManager</c>. That downloads ffmpeg and
///     launches a headless browser, none of which has any business happening while generating a migration.
///     No database connection is opened here: the provider is only needed so the model can be built.
/// </remarks>
public class PostgresContextDesignTimeFactory : IDesignTimeDbContextFactory<PostgresContext>
{
    public PostgresContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true)
            .AddJsonFile("appsettings.Development.json", true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("SyncStreamDB")
                               ?? Environment.GetEnvironmentVariable("ConnectionStrings__SyncStreamDB")
                               ?? "Host=localhost;Database=syncstream;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<PostgresContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PostgresContext(options);
    }
}
