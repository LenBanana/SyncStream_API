using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    public void Dispose()
    {
        _timer?.Dispose();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(UpdateServerHealthStatus, null, TimeSpan.Zero, General.ServerHealthTimeInSeconds);
    }


    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    //A method "UpdateServerHealthStatus" that regularly get the server health and sends it to the clients with a specific group name
    private async void UpdateServerHealthStatus(object state)
    {
        using var scope = _serviceScope.CreateScope();
        var serverHealth = HardwareUsage.GetServerHealth();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
        await hubContext.Clients.Group(General.AdminGroupName).serverHealth(serverHealth);
    }
}