﻿using Microsoft.Extensions.Configuration;
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

        public static async Task<string> ResolveURL(string url, string UniqueId, IConfiguration Configuration)
        {
            if (url.Contains("twitch.tv"))
            {
                if ((url.ToLower().StartsWith("http") && url.Count(x => x == '/') == 3) || url.Count(x => x == '/') == 1)
                    return url.Split('/').Last();
                else
                    return "v" + url.Split('/').Last();
            }
            string title = "";
            Uri uri = new Uri(url);
            string videokey = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("v");
            if (videokey == null)
                videokey = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("list");

            try
            {
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
            if (title.Length == 0)
            {
                try
                {
                    var section = Configuration.GetSection("YTKey");
                    string key = section.Value;
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            return title;
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
