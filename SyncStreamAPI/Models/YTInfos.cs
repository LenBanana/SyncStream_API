using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class YtVideoInfo
    {
        public VideoDetails VideoDetails { get; set; }
    }

    public class VideoDetails
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public long LengthSeconds { get; set; }
        public List<string> Keywords { get; set; }
        public string ChannelId { get; set; }
        public bool IsOwnerViewing { get; set; }
        public string ShortDescription { get; set; }
        public bool IsCrawlable { get; set; }
        public double AverageRating { get; set; }
        public bool AllowRatings { get; set; }
        public long ViewCount { get; set; }
        public string Author { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsUnpluggedCorpus { get; set; }
        public bool IsLiveContent { get; set; }
    }

    public static class YTInfoExtension
    {
        public static YtVideoInfo FromJson(this YtVideoInfo api, string json) => JsonConvert.DeserializeObject<YtVideoInfo>(json);
    }
}
