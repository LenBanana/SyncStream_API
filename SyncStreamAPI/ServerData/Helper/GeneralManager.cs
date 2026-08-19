using System;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.ServerData.Helper;

public class GeneralManager
{
    public GeneralManager(IServiceProvider serviceProvider, ConcurrentDictionary<string, Room> rooms)
    {
        _serviceProvider = serviceProvider;
        Rooms = rooms;
    }

    private IServiceProvider _serviceProvider { get; }
    public ConcurrentDictionary<string, Room> Rooms { get; set; } = new();

    public static void ReadSettings(IConfiguration config)
    {
        var section = config.GetSection("MaxParallelConversions");
        Convert.ToInt32(section.Value);
        section = config.GetSection("MaxParallelYtDownloads");
        General.MaxParallelYtDownloads = Convert.ToInt32(section.Value);
    }

    private void AddRoom(Room room)
    {
        Rooms.TryAdd(room.uniqueId, room);
    }

    public async void AddDefaultRooms()
    {
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                AddRoom(new Room("Dreckroom", "dreck", false, true));
                AddRoom(new Room("Randomkeller", "random", false, true));
                AddRoom(new Room("Guffelstübchen", "guffel", false, true));
                for (var i = 1; i <= General.GuestRoomAmount; i++)
                    AddRoom(new Room($"Guest Room - {i}", $"guest{i}", false, false));

                foreach (var room in _postgres.Rooms) AddRoom(room);

                if (await _postgres.Folders.CountAsync() == 0)
                {
                    _postgres.Folders.Add(new DbFileFolder("Default"));
                    await _postgres.SaveChangesAsync();
                }
            }
        }
        catch
        {
        }
    }
}