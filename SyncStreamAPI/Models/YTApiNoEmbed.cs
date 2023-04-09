using Newtonsoft.Json;
using System;

namespace SyncStreamAPI.Models
{
    public class YTApiNoEmbed
    {
        [JsonProperty("author_name")]
        public string AuthorName { get; set; }

        [JsonProperty("thumbnail_width")]
        public long ThumbnailWidth { get; set; }

        [JsonProperty("thumbnail_url")]
        public Uri ThumbnailUrl { get; set; }

        [JsonProperty("author_url")]
        public Uri AuthorUrl { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("url")]
        public Uri Url { get; set; }

        [JsonProperty("provider_name")]
        public string ProviderName { get; set; }

        [JsonProperty("provider_url")]
        public Uri ProviderUrl { get; set; }

        [JsonProperty("width")]
        public long Width { get; set; }

        [JsonProperty("thumbnail_height")]
        public long ThumbnailHeight { get; set; }

        [JsonProperty("height")]
        public long Height { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("html")]
        public string Html { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }
    }
    public static class NoEmbedExtension
    {
        public static YTApiNoEmbed FromJson(this YTApiNoEmbed api, string json) => JsonConvert.DeserializeObject<YTApiNoEmbed>(json);
    }
}
