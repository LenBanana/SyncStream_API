using System.Collections.Generic;
using Newtonsoft.Json;

namespace SyncStreamAPI.Models.Youtube;

public class YtApi
{
    public string kind { get; set; }
    public string etag { get; set; }
    public string nextPageToken { get; set; }
    public string prevPageToken { get; set; }
    public string regionCode { get; set; }
    public PageInfo pageInfo { get; set; }
    public List<Items> items { get; set; }

    public YtApi FromJson(string json)
    {
        return JsonConvert.DeserializeObject<YtApi>(json);
    }
}

public class PageInfo
{
    public int totalResults { get; set; }
    public int resultsPerPage { get; set; }
}

public class Items
{
    public string kind { get; set; }
    public string etag { get; set; }
    public Id id { get; set; }
    public Snippet snippet { get; set; }
}

public class Id
{
    public string kind { get; set; }
    public string videoId { get; set; }
    public string channelId { get; set; }
}

public class Snippet
{
    public string publishedAt { get; set; }
    public string channelId { get; set; }
    public string title { get; set; }
    public string description { get; set; }
    public Thumbnails thumbnails { get; set; }
    public string channelTitle { get; set; }
    public string liveBroadcastContent { get; set; }
    public string publishTime { get; set; }
}

public class Thumbnails
{
    [JsonProperty("default")] public Default DefaultThumbnail { get; set; }
    public Medium medium { get; set; }
    public High high { get; set; }
}

public class Default
{
    public string url { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

public class Medium
{
    public string url { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

public class High
{
    public string url { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}