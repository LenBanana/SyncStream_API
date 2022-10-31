using System;

namespace SyncStreamAPI.Models
{
    public class DownloadInfo
    {
        public string Id { get; set; }
        public double Progress { get; set; }
        public string Type { get; set; }
        public DownloadInfo(string type)
        {
            Id = Guid.NewGuid().ToString();
            Type = type;
        }
        public DownloadInfo(double progress, string type)
        {
            Progress = progress;
            Id = Guid.NewGuid().ToString();
            Type = type;
        }
    }
}
