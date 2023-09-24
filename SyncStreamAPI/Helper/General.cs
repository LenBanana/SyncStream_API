using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Enums;
using SyncStreamAPI.GallowGameWords;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.Models.Youtube;
using YoutubeDLSharp;

namespace SyncStreamAPI.Helper
{
    public class General
    {
        public static Random Random { get; } = new Random();
        public static string SystemMessageName { get; } = "Dreckbot";

        public static string FilePath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? System.IO.Directory.GetCurrentDirectory() + "/VideoFiles"
            : System.IO.Directory.GetCurrentDirectory() + "\\VideoFiles";

        public static string TemporaryFilePath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? System.IO.Directory.GetCurrentDirectory() + "/Temp"
            : System.IO.Directory.GetCurrentDirectory() + "\\Temp";

        public static string YtDlpUrl { get; } = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp";

        public static readonly string XmlMethodDescriptions = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? System.IO.Directory.GetCurrentDirectory() + "/PrivilegeMethodDescriptions.xml"
            : System.IO.Directory.GetCurrentDirectory() + "\\PrivilegeMethodDescriptions.xml";

        public static readonly int DefaultFolderId = 1;
        public static int GuestRoomAmount { get; } = 6;
        public static int MaxParallelConversions { get; set; } = 6;
        public static int MaxParallelYtDownloads { get; set; } = 4;

        public static TimeSpan DaysToKeepTemporaryFiles { get; } = TimeSpan.FromDays(14);
        public static TimeSpan MinutesToKeepFFmpeg { get; } = TimeSpan.FromMinutes(10);
        public static TimeSpan CheckIntervalInMinutes { get; } = TimeSpan.FromMinutes(1);
        public static TimeSpan ServerHealthTimeInSeconds { get; } = TimeSpan.FromSeconds(1);
        public static TimeSpan SecondsToKickMember { get; } = TimeSpan.FromSeconds(10);

        public static int GallowGameLength { get; } = 90;
        public static int GallowGameLengthMin { get; } = 60;
        public static int GallowGameLengthMax { get; } = 300;
        public static int GallowGuessPoints { get; } = 10;
        public static int GallowDrawBasePoints { get; } = 5;
        public static int GallowWordLengthMultiplierPlayer { get; } = 8;
        public static int GallowWordLengthMultiplierHost { get; } = 12;

        public static int BlackjackShoeSize { get; } = 6;
        public static string LoggedInGroupName { get; } = "approved";
        public static string BottedInGroupName { get; } = "dreckbots";
        public static string AdminGroupName { get; } = "admin";

        //Task timeout (ms)
        public static int FFmpegTimeout { get; } = 5000;

        private static Regex ytRegEx =
            new Regex(
                @"https?://(?:www\.|m\.)?youtube\.com/(?:watch\?(?=.*v=\w+)(?:\S+)?|playlist\?(?=.*list=\w+)(?:\S+)?|v/|embed/|attribution_link\?a=\w+&u=/watch\?v=\w+)|youtu\.be/(\w+)(?:\S+)?");

        public static bool IsYt(string url) => ytRegEx.IsMatch(url);
        public static HttpClient HttpClient { get; } = new HttpClient();

        public static YoutubeDL GetYoutubeDL()
        {
            var ytdl = new YoutubeDL();
            ytdl.FFmpegPath = GetFFmpegPath();
            ytdl.YoutubeDLPath = GetYtDlpPath();
            return ytdl;
        }

        public static string GetFFmpegPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "/app/ffmpeg";
            else
                return "ffmpeg.exe";
        }

        public static string GetYtDlpPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "/app/yt-dlp";
            else
                return "yt-dlp.exe";
        }

        public static string GetGallowWord(Language language)
        {
            var num = 0;
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

        public static async Task<string> ResolveUrl(string url, IConfiguration Configuration)
        {
            if (url.Contains("twitch.tv"))
            {
                if ((url.ToLower().StartsWith("http") && url.Count(x => x == '/') == 3) ||
                    url.Count(x => x == '/') == 1)
                {
                    return url.Split('/').Last();
                }
                else
                {
                    return "v" + url.Split('/').Last();
                }
            }

            if (url.Contains("playlist?list="))
            {
                return "Playlistvideo";
            }

            string title = "";
            title = (await NoEmbedYtApi(url)).Title;

            if (title == null || title.Length == 0)
            {
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
            }

            if (title.Length == 0)
            {
                try
                {
                    string videokey = GetYtVideoKey(url);
                    string infoUrl = "http://youtube.com/get_video_info?video_id=" + videokey;
                    using (WebClient client = new WebClient())
                    {
                        string source = "";
                        int i = 0;
                        while ((title.Length == 0 || title.ToLower().Trim() == "youtube") && i < 10)
                        {
                            source = client.DownloadString(infoUrl);
                            if (source.Length > 0)
                            {
                                List<string> attributes =
                                    source.Split('&')?.Select(x => HttpUtility.UrlDecode(x)).ToList();
                                int idx = attributes.FindIndex(x => x.StartsWith("player_response="));
                                if (idx != -1)
                                {
                                    YtVideoInfo videoInfo =
                                        new YtVideoInfo().FromJson(attributes[idx].Split(new[] { '=' }, 2)[1]);
                                    return videoInfo.VideoDetails.Title + " - " + videoInfo.VideoDetails.Author;
                                }
                            }

                            await Task.Delay(50);
                            i++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            if (title.Length == 0)
            {
                title = await YtApiInfo(url, Configuration);
            }

            return title;
        }

        public static string GetYtVideoKey(string url)
        {
            Uri uri = new Uri(url);
            string videokey = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("v");
            if (videokey == null)
            {
                videokey = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("list");
            }

            return videokey;
        }

        public static async Task<YTApiNoEmbed> NoEmbedYtApi(string url)
        {
            var apiResult = new YTApiNoEmbed();
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://noembed.com/embed?url=" + url);
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                using (System.IO.Stream stream = response.GetResponseStream())
                using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
                {
                    apiResult = new YTApiNoEmbed().FromJson(await reader.ReadToEndAsync());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return apiResult;
        }

        private static string WebRtcTurnServerPassword(IConfiguration Configuration)
        {
            return Configuration.GetSection("WebRtcPassword").Value;
        }

        public static WebRtcCredentials GenerateTemporaryCredentials(IConfiguration configuration, int ttlInSeconds = 3600)
        {
            var sharedSecret = WebRtcTurnServerPassword(configuration);
            var unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
            var username = (unixTimestamp + ttlInSeconds).ToString();

            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(sharedSecret));
            var password = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
            return new WebRtcCredentials(username, password);
        }

        private static async Task<string> YtApiInfo(string url, IConfiguration configuration)
        {
            try
            {
                string title = "";
                var section = configuration.GetSection("YTKey");
                string key = section.Value;
                string videokey = GetYtVideoKey(url);
                string Url = "https://www.googleapis.com/youtube/v3/videos?part=snippet&id=" + videokey + "&key=" + key;
                YtApi apiResult;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                using (System.IO.Stream stream = response.GetResponseStream())
                using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
                {
                    apiResult = new YtApi().FromJson(await reader.ReadToEndAsync());
                }

                if (apiResult != null && apiResult.items.Count > 0)
                {
                    title = apiResult.items.First().snippet.title + " - " +
                            apiResult.items.First().snippet.channelTitle;
                }

                return title;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return "";
            }
        }
    }
}