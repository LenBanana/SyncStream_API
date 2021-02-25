using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class VimeoResponse
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public string UploadDate { get; set; }
        public string ThumbnailSmall { get; set; }
        public string ThumbnailMedium { get; set; }
        public string ThumbnailLarge { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string UserUrl { get; set; }
        public string UserPortraitSmall { get; set; }
        public string UserPortraitMedium { get; set; }
        public string UserPortraitLarge { get; set; }
        public string UserPortraitHuge { get; set; }
        public string Duration { get; set; }
        public string Width { get; set; }
        public string Height { get; set; }
        public string Tags { get; set; }
        public string EmbedPrivacy { get; set; }
    }

    public static class Vimeo
    {
        public static VimeoResponse FromUrl(string url)
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.198 Safari/537.36");
                    string videokey = url.Split('/').Last();
                    string json = client.DownloadString("http://vimeo.com/api/v2/video/" + videokey + ".json");
                    json = json.Substring(1, json.Length - 2);
                    VimeoResponse result = JsonConvert.DeserializeObject<VimeoResponse>(json);
                    return result;
                }
            } catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }
    }
}
