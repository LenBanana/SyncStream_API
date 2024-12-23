﻿using System;
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
    public GeneralManager(IServiceProvider serviceProvider, BlockingCollection<Room> rooms)
    {
        _serviceProvider = serviceProvider;
        Rooms = rooms;
    }

    private IServiceProvider _serviceProvider { get; }
    public BlockingCollection<Room> Rooms { get; set; } = new();

    public static void ReadSettings(IConfiguration config)
    {
        var section = config.GetSection("MaxParallelConversions");
        Convert.ToInt32(section.Value);
        section = config.GetSection("MaxParallelYtDownloads");
        General.MaxParallelYtDownloads = Convert.ToInt32(section.Value);
    }

    public async void AddDefaultRooms()
    {
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                await _postgres.Database.EnsureCreatedAsync();
                Rooms.Add(new Room("Dreckroom", "dreck", false, true));
                Rooms.Add(new Room("Randomkeller", "random", false, true));
                Rooms.Add(new Room("Guffelstübchen", "guffel", false, true));
                for (var i = 1; i <= General.GuestRoomAmount; i++)
                    Rooms.Add(new Room($"Guest Room - {i}", $"guest{i}", false, false));

                foreach (var room in _postgres.Rooms) Rooms.Add(room);

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