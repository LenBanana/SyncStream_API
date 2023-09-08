using HtmlAgilityPack;
using PuppeteerSharp;
using SyncStreamAPI.Models;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper
{
    public class BrowserAutomation
    {
        IServiceProvider _serviceProvider { get; set; }
        public static IBrowser Browser { get; private set; } = Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = false,
            Args = new string[] {
                "--no-sandbox", 
                $"--load-extension=\"{(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "/app/ublock" : "C:\\Users\\Len\\AppData\\Local\\Google\\Chrome\\User Data\\Default\\Extensions\\cjpalhdlnbpafiamejdnhcphjbkeiagm\\1.51.0_1")}\"",
                "--autoplay-policy=no-user-gesture-required"
            },
            IgnoredDefaultArgs = new [] { "--disable-extensions" }
        }).Result;
        private static  NavigationOptions options { get; set; }

        public BrowserAutomation(IServiceProvider provider)
        {
            _serviceProvider = provider;
        }

        public static async void Init()
        {
            try
            {
                options = new NavigationOptions() { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } };
                BrowserFetcher browserFetcher = new BrowserFetcher();
                var dl = await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
                var path = dl.FolderPath + "/chrome-linux";
                Console.WriteLine($"Download to {path} was {dl.Downloaded}");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    LinuxBash.Bash($"chmod -R +x {path}");
                }
                Console.WriteLine("Successfully initiated browser");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error initiating browser\n\n" + ex.Message);
            }
        }

        public static async Task<BrowserM3U8Response> GetM3U8FromUrl(string url)
        {
            var result = new BrowserM3U8Response();
            result.InputUrl = url;
            var page = await Browser.NewPageAsync();
            page.Response += (sender, e) =>
            {
                if (e.Response.Url.ToLower().Contains("m3u8"))
                {
                    result.OutputUrls.Add(e.Response.Url.Trim());
                }
            };
            await page.GoToAsync(url, options);
            var html = await page.GetContentAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var videoNodes = doc.DocumentNode.SelectNodes("//video");
            if (videoNodes == null)
            {
                videoNodes = doc.DocumentNode.SelectNodes("//a");
            }

            if (videoNodes == null)
            {
                return new BrowserM3U8Response();
            }

            foreach (var node in videoNodes)
            {
                var src = node.GetAttributeValue("src", "").Trim();
                if (src.Length == 0)
                {
                    src = node.GetAttributeValue("href", "").Trim();
                }

                if (src.Length > 0 && !result.OutputUrls.Contains(src))
                {
                    result.OutputUrls.Add(src);
                }
            }
            await page.DisposeAsync();
            return result;
        }
    }
}
