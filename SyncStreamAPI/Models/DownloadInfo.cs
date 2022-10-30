using System;

namespace SyncStreamAPI.Models
{
    public class DownloadInfo
    {
        public string Id { get; set; }
        public double Progress { get; set; }
        public DownloadInfo()
        {
            Id = Guid.NewGuid().ToString();
        }
        public DownloadInfo(double progress)
        {
            Progress = progress;
            Id = Guid.NewGuid().ToString();
        }
    }
}
