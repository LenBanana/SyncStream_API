using System;

namespace SyncStreamAPI.PostgresModels;

public class DbApplicationVersion
{
    public int ID { get; set; }
    public string Version { get; set; }
    public string Name { get; set; }
    public DateTime LastUpdate { get; set; }
}