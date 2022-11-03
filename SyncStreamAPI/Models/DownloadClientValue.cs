using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class DownloadClientValue
    {
        public DownloadClientValue(int userId, string fileName, string connectionId, string token, string url)
        {
            UserId = userId;
            ConnectionId = connectionId;
            Token = token;
            FileName = fileName;
            Url = url;
            UniqueId = Guid.NewGuid().ToString();
            CancellationToken = new CancellationTokenSource();
            KeepUrlAlive();
        }
        public int UserId { get; set; }
        public string FileName { get; set; }
        public string Url { get; set; }
        public string ConnectionId { get; set; }
        public string Token { get; set; }
        public string UniqueId { get; set; }
        public Stopwatch Stopwatch { get; set; }
        public CancellationTokenSource CancellationToken { get; set; }
        WebClient keepAliveClient = new WebClient();
        public async void KeepUrlAlive()
        {
            if (Stopwatch == null)
            {
                try
                {
                   await keepAliveClient?.DownloadStringTaskAsync(new Uri(Url));
                }
                catch { }
                await Task.Delay(1000);
                KeepUrlAlive();
            }
            keepAliveClient?.Dispose();
        }
    }
}
