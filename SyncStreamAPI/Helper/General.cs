using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Enums;
using SyncStreamAPI.GallowGameWords;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.Models.Youtube;
using YoutubeDLSharp;

namespace SyncStreamAPI.Helper;

public static class General
{
    public static readonly string XmlMethodDescriptions = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        ? Directory.GetCurrentDirectory() + "/PrivilegeMethodDescriptions.xml"
        : Directory.GetCurrentDirectory() + "\\PrivilegeMethodDescriptions.xml";

    public static readonly int DefaultFolderId = 1;

    private static readonly Regex ytRegEx =
        new(
            @"https?://(?:www\.|m\.)?youtube\.com/(?:watch\?(?=.*v=\w+)(?:\S+)?|playlist\?(?=.*list=\w+)(?:\S+)?|v/|embed/|attribution_link\?a=\w+&u=/watch\?v=\w+)|youtu\.be/(\w+)(?:\S+)?");

    public static Random Random { get; } = new();
    public static string SystemMessageName => "Dreckbot";

    public static string FilePath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        ? Directory.GetCurrentDirectory() + "/VideoFiles"
        : Directory.GetCurrentDirectory() + "\\VideoFiles";

    public static string TemporaryFilePath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        ? Directory.GetCurrentDirectory() + "/Temp"
        : Directory.GetCurrentDirectory() + "\\Temp";

    public static string YtDlpUrl => "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp";
    public static int GuestRoomAmount => 6;
    public static int MaxParallelYtDownloads { get; set; } = 4;

    public static TimeSpan DaysToKeepTemporaryFiles { get; } = TimeSpan.FromDays(14);
    public static TimeSpan MinutesToKeepFFmpeg { get; } = TimeSpan.FromMinutes(10);
    public static TimeSpan CheckIntervalInMinutes { get; } = TimeSpan.FromMinutes(1);
    public static TimeSpan ServerHealthTimeInSeconds { get; } = TimeSpan.FromSeconds(1);
    public static TimeSpan SecondsToKickMember { get; } = TimeSpan.FromSeconds(10);

    public static int GallowGameLength => 90;
    public static int GallowGameLengthMin => 60;
    public static int GallowGameLengthMax => 300;
    public static int GallowGuessPoints => 10;
    public static int GallowDrawBasePoints => 5;
    public static int GallowWordLengthMultiplierPlayer => 8;
    public static int GallowWordLengthMultiplierHost => 12;

    public static int BlackjackShoeSize => 6;
    public static string LoggedInGroupName => "approved";
    public static string BottedInGroupName => "dreckbots";
    public static string AdminGroupName => "admin";

    //Task timeout (ms)
    public static int FFmpegTimeout => 5000;

    public static YoutubeDL GetYoutubeDl()
    {
        var ytDl = new YoutubeDL
        {
            FFmpegPath = GetFFmpegPath(),
            YoutubeDLPath = GetYtDlpPath()
        };
        return ytDl;
    }

    public static string GetFFmpegPath()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "/app/ffmpeg" : "ffmpeg.exe";
    }

    private static string GetYtDlpPath()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "/app/yt-dlp" : "yt-dlp.exe";
    }

    public static string GetGallowWord(Language language)
    {
        int num;
        switch (language)
        {
            case Language.German:
                num = Random.Next(0, GermanGallowWords.GermanGallowWordList.Count - 1);
                return GermanGallowWords.GermanGallowWordList[num].FirstCharToUpper();
            case Language.English:
                num = Random.Next(0, EnglishGallowWords.EnglishGallowWordList.Count - 1);
                return EnglishGallowWords.EnglishGallowWordList[num].FirstCharToUpper();
            default:
                return "";
        }
    }

    public static async Task<string> ResolveUrl(string url, IConfiguration configuration)
    {
        if (url.Contains("twitch.tv"))
        {
            if ((url.ToLower().StartsWith("http") && url.Count(x => x == '/') == 3) ||
                url.Count(x => x == '/') == 1)
                return url.Split('/').Last();

            return "v" + url.Split('/').Last();
        }

        if (url.Contains("playlist?list=")) return "Playlistvideo";

        var title = (await NoEmbedYtApi(url)).Title;

        if (string.IsNullOrEmpty(title))
            try
            {
                var webGet = new HtmlWeb();
                var document = webGet.Load(url);
                title = document.DocumentNode.SelectSingleNode("html/head/title").InnerText;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

        if (title is { Length: 0 })
            try
            {
                var videoKey = GetYtVideoKey(url);
                var infoUrl = "http://youtube.com/get_video_info?video_id=" + videoKey;
                using var client = new HttpClient();
                var i = 0;
                while ((title.Length == 0 || title.ToLower().Trim() == "youtube") && i < 10)
                {
                    var source = await client.GetStringAsync(infoUrl);
                    if (source.Length > 0)
                    {
                        var attributes =
                            source.Split('&')?.Select(HttpUtility.UrlDecode).ToList();
                        var idx = attributes.FindIndex(x => x.StartsWith("player_response="));
                        if (idx != -1)
                        {
                            var videoInfo =
                                new YtVideoInfo().FromJson(attributes[idx].Split(new[] { '=' }, 2)[1]);
                            return videoInfo.VideoDetails.Title + " - " + videoInfo.VideoDetails.Author;
                        }
                    }

                    await Task.Delay(50);
                    i++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

        if (title is { Length: 0 }) title = await YtApiInfo(url, configuration);

        return title;
    }

    private static string GetYtVideoKey(string url)
    {
        var uri = new Uri(url);
        var videoKey = HttpUtility.ParseQueryString(uri.Query).Get("v") ??
                       HttpUtility.ParseQueryString(uri.Query).Get("list");

        return videoKey;
    }

    private static async Task<YTApiNoEmbed> NoEmbedYtApi(string url)
    {
        var apiResult = new YTApiNoEmbed();
        try
        {
            var uri = "https://noembed.com/embed?url=" + url;
            using var client = new HttpClient();
            apiResult = new YTApiNoEmbed().FromJson(await client.GetStringAsync(uri));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }

        return apiResult;
    }

    private static string WebRtcTurnServerPassword(IConfiguration configuration)
    {
        return configuration.GetSection("WebRtcPassword").Value;
    }

    public static WebRtcCredentials GenerateTemporaryCredentials(IConfiguration configuration,
        int ttlInSeconds = 3600)
    {
        var sharedSecret = WebRtcTurnServerPassword(configuration);
        var unixTimestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        var username = (unixTimestamp + ttlInSeconds).ToString();

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(sharedSecret));
        var password = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
        return new WebRtcCredentials(username, password);
    }

    private static async Task<string> YtApiInfo(string url, IConfiguration configuration)
    {
        try
        {
            var title = "";
            var section = configuration.GetSection("YTKey");
            var key = section.Value;
            var videoKey = GetYtVideoKey(url);
            var uri = "https://www.googleapis.com/youtube/v3/videos?part=snippet&id=" + videoKey + "&key=" + key;
            YtApi apiResult;
            using (var client = new HttpClient())
            {
                apiResult = new YtApi().FromJson(await client.GetStringAsync(uri));
            }

            if (apiResult != null && apiResult.items.Count > 0)
                title = apiResult.items.First().snippet.title + " - " +
                        apiResult.items.First().snippet.channelTitle;

            return title;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return "";
        }
    }
}