using System;

namespace SyncStreamAPI.Models
{
    public class DownloadClientValue
    {
        public DownloadClientValue(string fileName, string connectionId, string token, string url)
        {
            ConnectionId = connectionId;
            Token = token;
            FileName = fileName;
            Url = url;
            UniqueId = Guid.NewGuid().ToString();
        }

        public string FileName { get; set; }
        public string Url { get; set; }
        public string ConnectionId { get; set; }
        public string Token { get; set; }
        public string UniqueId { get; set; }
    }
}
