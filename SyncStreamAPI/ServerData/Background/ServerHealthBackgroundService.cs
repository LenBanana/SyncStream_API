using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Helper.ServerHealth;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;

namespace SyncStreamAPI.ServerData.Background;

public class ServerHealthBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _serviceScope;
    private Timer _timer;

    public ServerHealthBackgroundService(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScope = serviceScopeFactory;
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using (var scope = _serviceScope.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PostgresContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }
        _timer = new Timer(UpdateServerHealthStatus, null, TimeSpan.Zero, General.ServerHealthTimeInSeconds);
    }
    
    //A method "UpdateServerHealthStatus" that regularly get the server health and sends it to the clients with a specific group name
    private async void UpdateServerHealthStatus(object state)
    {
        using var scope = _serviceScope.CreateScope();
        var serverHealth = await HardwareUsage.GetServerHealth();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
        await hubContext.Clients.Group(General.AdminGroupName).serverHealth(serverHealth);
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