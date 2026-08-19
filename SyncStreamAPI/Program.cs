using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SyncStreamAPI.DataContext;

namespace SyncStreamAPI;

public class Program
{
    public static void Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        // Schema first, once, before anything resolves a context. This runs here rather than in
        // Startup.Configure because MainManager is constructed as a parameter of Configure and touches
        // the database from its own constructor, so by the time Configure's body runs it is already too
        // late. It replaces two EnsureCreated calls that used to race each other.
        using (var scope = host.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PostgresContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<PostgresContext>>();
            MigrationBootstrapper.Run(context, logger);

            // After the schema, because it reads and writes rows the migrations may have just reshaped.
            StoredSecretUpgrade.Run(context, logger);
        }

        host.Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
    }
}
