using System;

namespace SyncStreamAPI.Models
{
    public class DownloadInfo
    {
        public string Id { get; set; }
        public double Progress { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public DownloadInfo(string type, string name)
        {
            Id = Guid.NewGuid().ToString();
            Type = type;
            Name = name;
        }
        public DownloadInfo(double progress, string type, string name)
        {
            Progress = progress;
            Id = Guid.NewGuid().ToString();
            Type = type;
            Name = name;
        }
    }
}
