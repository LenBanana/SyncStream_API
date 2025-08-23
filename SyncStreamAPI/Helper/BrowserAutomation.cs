using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HtmlAgilityPack;
using PuppeteerSharp;
using SyncStreamAPI.Models;

namespace SyncStreamAPI.Helper;

public class BrowserAutomation
{
    public BrowserAutomation(IServiceProvider provider)
    {
        ServiceProvider = provider;
    }

    private IServiceProvider ServiceProvider { get; set; }

    public static IBrowser Browser { get; } = Puppeteer.LaunchAsync(new LaunchOptions
    {
        Headless = true,
        Args =
        [
            "--no-sandbox",
            $"--load-extension=\"{(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "/app/ublock" : "C:\\Users\\Len\\AppData\\Local\\Google\\Chrome\\User Data\\Default\\Extensions\\cjpalhdlnbpafiamejdnhcphjbkeiagm\\1.51.0_1")}\"",
            "--autoplay-policy=no-user-gesture-required"
        ],
        IgnoredDefaultArgs = ["--disable-extensions"]
    }).Result;

    private static NavigationOptions? Options { get; set; }

    public static async void Init()
    {
        try
        {
            Options = new NavigationOptions { WaitUntil = [WaitUntilNavigation.Networkidle2] };
            var browserFetcher = new BrowserFetcher();
            var dl = await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
            var path = dl.FolderPath + "/chrome-linux";
            Console.WriteLine($"Download to {path} was {dl.Downloaded}");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) LinuxBash.Bash($"chmod -R +x {path}");
            Console.WriteLine("Successfully initiated browser");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error initiating browser\n\n" + ex.Message + "\n\n" + ex.InnerException?.Message);
        }
    }

    public static async Task<BrowserM3U8Response> GetM3U8FromUrl(string url)
    {
        var result = new BrowserM3U8Response
        {
            InputUrl = url
        };
        var page = await Browser.NewPageAsync();
        page.Response += (_, e) =>
        {
            if (e.Response.Url.Contains("m3u8", StringComparison.CurrentCultureIgnoreCase))
                result.OutputUrls.Add(e.Response.Url.Trim());
        };
        await page.GoToAsync(url, Options);
        var html = await page.GetContentAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var videoNodes = doc.DocumentNode.SelectNodes("//video") ?? doc.DocumentNode.SelectNodes("//a");

        if (videoNodes == null) return new BrowserM3U8Response();

        foreach (var node in videoNodes)
        {
            var src = node.GetAttributeValue("src", "").Trim();
            if (src.Length == 0) src = node.GetAttributeValue("href", "").Trim();

            if (src.Length > 0 && !result.OutputUrls.Contains(src)) result.OutputUrls.Add(src);
        }

        await page.DisposeAsync();
        return result;
    }
}