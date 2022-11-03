using System;
using System.Diagnostics;
using System.Threading;

namespace SyncStreamAPI.Models
{
    public class DownloadClientValue
    {
        public DownloadClientValue(int userId, string fileName, string connectionId, string token, string url, Stopwatch stopwatch)
        {
            UserId = userId;
            ConnectionId = connectionId;
            Token = token;
            FileName = fileName;
            Url = url;
            UniqueId = Guid.NewGuid().ToString();
            Stopwatch = stopwatch;
            CancellationToken = new CancellationTokenSource();
        }
        public int UserId { get; set; }
        public string FileName { get; set; }
        public string Url { get; set; }
        public string ConnectionId { get; set; }
        public string Token { get; set; }
        public string UniqueId { get; set; }
        public Stopwatch Stopwatch { get; set; }
        public CancellationTokenSource CancellationToken { get; set; }
    }
}
