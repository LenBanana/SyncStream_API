using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;

namespace SyncStreamAPI.ServerData.Background
{
    public class DataBackgroundService : IHostedService, IDisposable
    {
        private readonly IServiceScopeFactory _serviceScope;
        private Timer _timer;

        public DataBackgroundService(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScope = serviceScopeFactory;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(CheckDatabaseAndRemoveFiles, null, TimeSpan.Zero, TimeSpan.FromMinutes(General.CheckIntervalInMinutes));
            return Task.CompletedTask;
        }

        private async void CheckDatabaseAndRemoveFiles(object state)
        {
            using (var scope = _serviceScope.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var thresholdDate = DateTime.UtcNow.AddDays(-General.DaysToKeepImages);
                var outdatedImages = await dbContext.Files.Where(e => e.Created < thresholdDate && e.Temporary).ToListAsync();
                foreach (var image in outdatedImages)
                {
                    var imgPath = image.GetPath();
                    if (File.Exists(imgPath))
                        File.Delete(imgPath);
                    dbContext.Files.Remove(image);
                }
                await dbContext.SaveChangesAsync();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
