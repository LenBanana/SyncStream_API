using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Enums;
using SyncStreamAPI.GallowGameWords;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace SyncStreamAPI.Helper
{
    public class General
    {
        private static Random random = new Random();        

        public static int GallowGameLength = 90;
        public static int GallowGuessPoints = 10;
        public static int GallowDrawBasePoints = 5;
        public static int GallowWordLengthMultiplierPlayer = 8;
        public static int GallowWordLengthMultiplierHost = 12;

        public static string GetGallowWord(Language language)
        {
            var num = 0;
            switch (language)
            {
                case Language.German:
                    num = random.Next(0, GermanGallowWords.GermanGallowWordList.Count - 1);
                    return GermanGallowWords.GermanGallowWordList[num].FirstCharToUpper();
                case Language.English:
                    num = random.Next(0, EnglishGallowWords.EnglishGallowWordList.Count - 1);
                    return EnglishGallowWords.EnglishGallowWordList[num].FirstCharToUpper();
                default:
                    return "";
            }
        }

        public static async Task<string> ResolveURL(string url, IConfiguration Configuration)
        {
            if (url.Contains("twitch.tv"))
            {
                if ((url.ToLower().StartsWith("http") && url.Count(x => x == '/') == 3) || url.Count(x => x == '/') == 1)
                    return url.Split('/').Last();
                else
                    return "v" + url.Split('/').Last();
            }
            string title = "";
            title = (await NoEmbedYTApi(url)).Title;

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
                    Console.WriteLine(ex.Message);
                }
            }

            if (title.Length == 0)
            {
                try
                {
                    string videokey = GetYTVideoKey(url);
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
                                List<string> attributes = source.Split('&').Select(x => HttpUtility.UrlDecode(x)).ToList();
                                int idx = attributes.FindIndex(x => x.StartsWith("player_response="));
                                if (idx != -1)
                                {
                                    YtVideoInfo videoInfo = new YtVideoInfo().FromJson(attributes[idx].Split(new[] { '=' }, 2)[1]);
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
                    Console.WriteLine(ex.Message);
                }
            }
            if (title.Length == 0)
            {
                title = await YTApiInfo(url, Configuration);
            }
            return title;
        }

        public static string GetYTVideoKey(string url)
        {
            Uri uri = new Uri(url);
            string videokey = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("v");
            if (videokey == null)
                videokey = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("list");
            return videokey;
        }

        public async static Task<YTApiNoEmbed> NoEmbedYTApi(string url)
        {
            YTApiNoEmbed apiResult = new YTApiNoEmbed();
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
                Console.WriteLine(ex.Message);
            }
            return apiResult;
        }

        public static async Task<string> YTApiInfo(string url, IConfiguration Configuration)
        {
            try
            {
                string title = "";
                var section = Configuration.GetSection("YTKey");
                string key = section.Value;
                string videokey = GetYTVideoKey(url);
                string Url = "https://www.googleapis.com/youtube/v3/videos?part=snippet&id=" + videokey + "&key=" + key;
                Ytapi apiResult;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                using (System.IO.Stream stream = response.GetResponseStream())
                using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
                {
                    apiResult = new Ytapi().FromJson(await reader.ReadToEndAsync());
                }
                if (apiResult != null && apiResult.Items.Count > 0)
                    title = apiResult.Items.First().Snippet.Title + " - " + apiResult.Items.First().Snippet.ChannelTitle;
                return title;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return "";
            }
        }

        public static async Task<(string title, string source)> ResolveTitle(string url, int maxTries)
        {
            try
            {
                string title = "";
                using (WebClient client = new WebClient())
                {
                    string source = "";
                    int i = 0;
                    while ((title.Length == 0 || title.ToLower().Trim() == "youtube") && i < maxTries)
                    {
                        source = client.DownloadString(url);
                        title = System.Text.RegularExpressions.Regex.Match(source, @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups["Title"].Value;
                        await Task.Delay(50);
                        i++;
                    }
                    if (title.Length == 0)
                        title = "External source";
                    return (title, source);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return ("External source", "");
            }
        }
    }
}
