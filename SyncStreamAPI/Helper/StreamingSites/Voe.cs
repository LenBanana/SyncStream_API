using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using PuppeteerSharp;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.Interfaces;
using SyncStreamAPI.Models.StreamExtraction;

namespace SyncStreamAPI.Helper.StreamingSites;

public class Voe : IStreamDownloader
{
    public async Task<DownloadExtract> GetDownloadLink(DownloadClientValue client)
    {
        try
        {
            if (!client.Url.Contains("/e/"))
                client.Url = client.Url.Replace("https://yodelswartlike.com/", "https://yodelswartlike.com/e/")
                    .Replace("https://voe.sx/", "https://voe.sx/e/");
            var downloadUri = "";
            var page = await BrowserAutomation.Browser.NewPageAsync();
            await page.GoToAsync(client.Url);
            await page.WaitForSelectorAsync("div.plyr__controls", new WaitForSelectorOptions { Timeout = 2500 });
            var html = await page.GetContentAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var pattern = @"'hls'\s*:\s*'([^']+)'";
            var match = Regex.Match(html, pattern);
            if (match.Success)
                downloadUri = match.Groups[1].Value;
            await page.CloseAsync();
            return new DownloadExtract
            {
                DownloadLink = downloadUri
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return new DownloadExtract
            {
                DownloadLink = client.Url
            };
        }
    }
}