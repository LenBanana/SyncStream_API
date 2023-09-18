using System;

namespace SyncStreamAPI.Models.ServerHealth;

public class ServerHealthDto
{
    public ServerHealthDto(double diskUsage, double memoryUsage, double cpuUsage, string upTime)
    {
        DiskUsage = diskUsage;
        MemoryUsage = memoryUsage;
        CpuUsage = cpuUsage;
        UpTime = upTime;
    }
    
    public ServerHealthDto(string upTime)
    {
        UpTime = upTime;
    }

    public double DiskUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double CpuUsage { get; set; }
    public string UpTime { get; set; }
}