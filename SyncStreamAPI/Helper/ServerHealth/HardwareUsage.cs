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
    public static async Task<ServerHealthDto> GetServerHealth()
    {
        var process = Process.GetCurrentProcess();
        var diskUsage = GetDiskUsage(process);
        var memoryUsage = await GetMemoryUsage(process);
        var cpuUsage = GetCpuUsage(process);
        var upTime = DateTime.Now - process.StartTime;
        var formattedUpTime = upTime.ToString(@"dd\:hh\:mm\:ss");
        return new ServerHealthDto(diskUsage, memoryUsage, cpuUsage, formattedUpTime);
    }

    private static double GetDiskUsage(Process process)
    {
        var driveLetter = Path.GetPathRoot(process.MainModule.FileName);
        DriveInfo driveInfo = new DriveInfo(driveLetter);
        var usedSpace = driveInfo.TotalSize - driveInfo.TotalFreeSpace;
        var totalSpace = driveInfo.TotalSize;
        // Calculate the percentage
        var percentDiskUsed = 100 - ((double)usedSpace / totalSpace * 100);
        return percentDiskUsed;
    }

    static long GetTotalMemoryOnLinux()
    {
        // Read from /proc/meminfo
        var meminfo = File.ReadAllText("/proc/meminfo");
        var memTotalLine = meminfo.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        var memTotalValue = memTotalLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];

        return long.Parse(memTotalValue) * 1024; // Convert from KB to Bytes
    }


    static long GetTotalMemoryOnWindows()
    {
        // Use the GlobalMemoryStatusEx API to get total physical memory on Windows
        MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memStatus))
        {
            return (long)memStatus.ullTotalPhys;
        }
        else
        {
            throw new Exception("Unable to retrieve total memory on Windows.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private static async Task<double> GetMemoryUsage(Process process)
    {
        var memoryUsedByProcess = process.WorkingSet64;
        var totalPhysicalMemory = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? GetTotalMemoryOnLinux()
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? GetTotalMemoryOnWindows()
                : throw new NotSupportedException("OS not supported");
        var percentMemoryUsed = (double)memoryUsedByProcess / totalPhysicalMemory * 100;
        return percentMemoryUsed;
    }

    private static double GetCpuUsage(Process process)
    {
        using PerformanceCounter cpuCounter =
            new("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName);
        var currentCpuUsage = cpuCounter.NextValue() / Environment.ProcessorCount;
        return currentCpuUsage;
    }
}