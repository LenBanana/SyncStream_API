﻿using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;

namespace SyncStreamAPI.ServerData.Background;

public class DataBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _serviceScope;
    private Timer _timer;

    public DataBackgroundService(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScope = serviceScopeFactory;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using (var scope = _serviceScope.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PostgresContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        _timer = new Timer(CheckDatabaseAndRemoveFiles, null, TimeSpan.Zero, General.CheckIntervalInMinutes);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private async void CheckDatabaseAndRemoveFiles(object state)
    {
        using var scope = _serviceScope.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostgresContext>();
        var thresholdDate = DateTime.UtcNow;
        var outdatedImages = await dbContext.Files.Where(e => e.DateToBeDeleted < thresholdDate && e.Temporary)
            .ToListAsync();
        foreach (var image in outdatedImages)
        {
            var imgPath = image.GetPath();
            if (File.Exists(imgPath)) File.Delete(imgPath);
            dbContext.Files.Remove(image);
        }

        await dbContext.SaveChangesAsync();
        // Inform the user of the deleted file if connected
        var imageUpdates = outdatedImages.DistinctBy(image => image.DbUserID).ToList();
        foreach (var imgUpdate in imageUpdates)
        {
            var dbUser = dbContext.Users?.FirstOrDefault(x => x.ID == imgUpdate.DbUserID);
            if (dbUser == null) continue;
            {
                var shareFolders = dbContext.FolderShare?.Where(x => x.DbUserID == dbUser.ID);
                var dbFolder = dbContext.Folders?.Where(x =>
                    x.DbUserID == null || x.DbUserID == dbUser.ID ||
                    shareFolders.FirstOrDefault(y => y.DbFolderID == x.Id) != null ||
                    shareFolders.FirstOrDefault(y => y.DbFolderID == x.ParentId) != null).OrderBy(x => x.Name).ToList();
                if (dbFolder == null) continue;
                {
                    var resultFolder = dbFolder.FirstOrDefault(x => x.Id == imgUpdate.DbFileFolderId);
                    if (resultFolder == null) continue;
                    var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                    await hub.Clients.Group(dbUser.ID.ToString()).getFolders(new FolderDto(resultFolder));
                }
            }
        }
    }
}