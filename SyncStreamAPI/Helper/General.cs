using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper
{
    public class General
    {

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
