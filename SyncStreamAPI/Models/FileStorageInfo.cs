using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace SyncStreamAPI.Models
{
    public class FileStorageInfo
    {
        public string Path { get; set; }
        public long TotalSize { get; set; }
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; }
        public List<string> LargestFiles { get; set; }
        public long TotalDiskSpace { get; set; }

        public FileStorageInfo(string path)
        {
            Path = path;
            TotalSize = 0;
            FileCount = 0;
            DirectoryCount = 0;
            LargestFiles = new List<string>();
            TotalDiskSpace = 0;
        }

        public void CalculateStorageInfo()
        {
            if (!Directory.Exists(Path))
            {
                throw new DirectoryNotFoundException($"The directory '{Path}' does not exist.");
            }

            // Get all files and directories in the given path, including subdirectories
            string[] allFiles = Directory.GetFiles(Path, "*", SearchOption.AllDirectories);
            string[] allDirectories = Directory.GetDirectories(Path, "*", SearchOption.AllDirectories);

            // Calculate the total size of all files in bytes
            foreach (string file in allFiles)
            {
                FileInfo fileInfo = new FileInfo(file);
                TotalSize += fileInfo.Length;
                FileCount++;
            }

            // Count the number of directories
            DirectoryCount = allDirectories.Length;

            // Sort files by size in descending order
            const int MAX_FILES_TO_KEEP = 10;
            var filesBySize = allFiles.Select(f => new FileInfo(f))
                                       .OrderByDescending(f => f.Length);

            // Store the largest files
            LargestFiles = new List<string>();
            int count = 0;
            foreach (var fileInfo in filesBySize)
            {
                if (count >= MAX_FILES_TO_KEEP)
                {
                    break;
                }
                LargestFiles.Add($"{fileInfo.Name} ({fileInfo.Length} bytes)");
                count++;
            }

            // Calculate the total disk space used by the directory
            const long BYTES_PER_KB = 1024;
            const long OVERHEAD_FACTOR = 4096; // This is the default allocation size for NTFS
            long totalDiskSpace = TotalSize + (allFiles.Length * OVERHEAD_FACTOR);
            TotalDiskSpace = totalDiskSpace / BYTES_PER_KB; // Convert to KB
        }
    }
}
