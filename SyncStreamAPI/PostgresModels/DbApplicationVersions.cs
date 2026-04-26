using System;

namespace SyncStreamAPI.PostgresModels;

public class DbApplicationVersion
{
    public int ID { get; set; }
    public string Version { get; set; }
    public string Name { get; set; }
    public DateTime LastUpdate { get; set; }
    // Nullable — apply via: ALTER TABLE "AppVersions" ADD COLUMN IF NOT EXISTS "ReleaseNotes" TEXT;
    public string? ReleaseNotes { get; set; }
}