using HtmlAgilityPack;
using PuppeteerSharp;
using SyncStreamAPI.Models;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper
{
    public class BrowserAutomation : IDisposable
    {
        IServiceProvider _serviceProvider { get; set; }
        public IBrowser browser { get; private set; }
        public NavigationOptions options { get; private set; }

        public BrowserAutomation(IServiceProvider provider)
        {
            _serviceProvider = provider;
        }

        public async void Init()
        {
            try
            {
                options = new NavigationOptions() { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } };
                BrowserFetcher browserFetcher = new BrowserFetcher();
                var dl = await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
                var path = dl.FolderPath + "/chrome-linux";                
                Console.WriteLine($"Download to {path} was {dl.Downloaded}");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    LinuxBash.Bash($"chmod -R +x {path}");
                browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    Args = new string[] {
                        "--user-agent=\"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.138 Safari/537.36\"",
                        "--disable-gpu",
                        "--disable-dev-shm-usage",
                        "--disable-setuid-sandbox",
                        "--no-sandbox"
                    }
                });
                Console.WriteLine("Successfully initiated browser");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error initiating browser\n\n" + ex.Message);
            }
        }

        public async Task<BrowserM3U8Response> GetM3U8FromUrl(string url)
        {
            if (browser == null)
                return new BrowserM3U8Response();
            var result = new BrowserM3U8Response();
            result.InputUrl = url;
            var page = await browser.NewPageAsync();
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
                videoNodes = doc.DocumentNode.SelectNodes("//a");
            if (videoNodes == null)
                return new BrowserM3U8Response();
            foreach (var node in videoNodes)
            {
                var src = node.GetAttributeValue("src", "").Trim();
                if (src.Length == 0)
                    src = node.GetAttributeValue("href", "").Trim();
                if (src.Length > 0 && !result.OutputUrls.Contains(src))
                    result.OutputUrls.Add(src);
            }
            await page.DisposeAsync();
            return result;
        }

        public void Dispose()
        {
            Console.WriteLine("Disposing browser");
            browser?.Dispose();
        }
    }
}
