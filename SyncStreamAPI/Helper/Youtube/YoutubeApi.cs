using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Models.Youtube;

namespace SyncStreamAPI.Helper.Youtube;

public class YoutubeApi
{
    private const string BaseUrl = "https://www.googleapis.com/youtube/v3/";
    private string _ytApiKey;

    public YoutubeApi(IConfiguration configuration)
    {
        var ytApiKey = configuration.GetSection("YTKey");
        _ytApiKey = ytApiKey.Value;
    }

    public async Task<YtApi> Search(string query, string nextPageToken = "", int pageSize = 10,
        string order = "relevance")
    {
        using var httpClient = new HttpClient();
        var url =
            $"{BaseUrl}search?part=snippet&type=video&maxResults={pageSize}&q={query}&key={_ytApiKey}&order={order}";
        if (!string.IsNullOrEmpty(nextPageToken))
            url += $"&pageToken={nextPageToken}";
        try
        {
            var response = await httpClient.GetStringAsync(url);
            var jsonResponse = JsonSerializer.Deserialize<YtApi>(response);
            return jsonResponse;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected Error: {ex.Message}");
        }

        return new YtApi();
    }
}