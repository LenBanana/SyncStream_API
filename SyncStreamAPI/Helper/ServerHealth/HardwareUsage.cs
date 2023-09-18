using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SyncStreamAPI.Models.ServerHealth;

namespace SyncStreamAPI.Helper.ServerHealth;

public class HardwareUsage
{
    //Implement GetDiskUsage, GetMemoryUsage, and GetCpuUsage

    // Get the disk usage, memory usage, and cpu usage of the application and return it as a ServerHealthDto in a single method
    public static ServerHealthDto GetServerHealth()
    {
        var process = Process.GetCurrentProcess();
        var upTime = DateTime.Now - process.StartTime;
        var formattedUpTime = upTime.ToString(@"dd\:hh\:mm\:ss");
        return new ServerHealthDto(formattedUpTime);
    }
}