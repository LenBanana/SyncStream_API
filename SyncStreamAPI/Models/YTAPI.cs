using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace SyncStreamAPI.Models
{
    public partial class Ytapi
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("etag")]
        public string Etag { get; set; }

        [JsonProperty("items")]
        public List<Item> Items { get; set; }

        [JsonProperty("pageInfo")]
        public PageInfo PageInfo { get; set; }
    }

    public static class YtapiExtension
    {
        public static Ytapi FromJson(this Ytapi api, string json) => JsonConvert.DeserializeObject<Ytapi>(json);
    }

    public partial class Item
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("etag")]
        public string Etag { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("snippet")]
        public Snippet Snippet { get; set; }
    }

    public partial class Snippet
    {
        [JsonProperty("publishedAt")]
        public DateTimeOffset PublishedAt { get; set; }

        [JsonProperty("channelId")]
        public string ChannelId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("thumbnails")]
        public Thumbnails Thumbnails { get; set; }

        [JsonProperty("channelTitle")]
        public string ChannelTitle { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("categoryId")]
        public string CategoryId { get; set; }

        [JsonProperty("liveBroadcastContent")]
        public string LiveBroadcastContent { get; set; }

        [JsonProperty("defaultLanguage")]
        public string DefaultLanguage { get; set; }

        [JsonProperty("localized")]
        public Localized Localized { get; set; }

        [JsonProperty("defaultAudioLanguage")]
        public string DefaultAudioLanguage { get; set; }
    }

    public partial class Localized
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public partial class Thumbnails
    {
        [JsonProperty("default")]
        public Default Default { get; set; }

        [JsonProperty("medium")]
        public Default Medium { get; set; }

        [JsonProperty("high")]
        public Default High { get; set; }

        [JsonProperty("standard")]
        public Default Standard { get; set; }

        [JsonProperty("maxres")]
        public Default Maxres { get; set; }
    }

    public partial class Default
    {
        [JsonProperty("url")]
        public Uri Url { get; set; }

        [JsonProperty("width")]
        public long Width { get; set; }

        [JsonProperty("height")]
        public long Height { get; set; }
    }

    public partial class PageInfo
    {
        [JsonProperty("totalResults")]
        public long TotalResults { get; set; }

        [JsonProperty("resultsPerPage")]
        public long ResultsPerPage { get; set; }
    }
}
