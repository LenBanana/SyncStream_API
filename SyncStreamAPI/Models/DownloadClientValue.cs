using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace SyncStreamAPI.Models
{
    public class DownloadClientValue
    {
        public DownloadClientValue(int userId, string fileName, string connectionId, string token, string url, ConversionPreset preset)
        {
            UserId = userId;
            Preset = preset;
            ConnectionId = connectionId;
            Token = token;
            FileName = fileName;
            Url = url;
            Running = false;
            UniqueId = Guid.NewGuid().ToString();
            CancellationToken = new CancellationTokenSource();
            KeepUrlAlive();
        }
        public DownloadClientValue(int userId, string fileName, string connectionId, string token, string url, string quality)
        {
            UserId = userId;
            Preset = ConversionPreset.Faster;
            Quality = quality;
            ConnectionId = connectionId;
            Token = token;
            FileName = fileName;
            Url = url;
            Running = false;
            UniqueId = Guid.NewGuid().ToString();
            CancellationToken = new CancellationTokenSource();
            KeepUrlAlive();
        }
        public int UserId { get; set; }
        public ConversionPreset Preset { get; set; }
        public string Quality { get; set; }
        public string FileName { get; set; }
        public string Url { get; set; }
        public string ConnectionId { get; set; }
        public string Token { get; set; }
        public string UniqueId { get; set; }
        public bool Running { get; set; }
        public Stopwatch Stopwatch { get; set; }
        public CancellationTokenSource CancellationToken { get; set; }
        WebClient keepAliveClient = new WebClient();
        bool stopKeepAlive = false;
        async void KeepUrlAlive()
        {
            if (Stopwatch == null && !stopKeepAlive)
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

        public void StopKeepAlive()
        {
            stopKeepAlive = true;
        }
    }
}
