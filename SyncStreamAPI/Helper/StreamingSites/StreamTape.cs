using System;
using System.Threading.Tasks;
using HtmlAgilityPack;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.Interfaces;
using SyncStreamAPI.Models.StreamExtraction;

namespace SyncStreamAPI.Helper.StreamingSites
{
    public class StreamTape : IStreamDownloader
    {
        public async Task<DownloadExtract> GetDownloadLink(DownloadClientValue client)
        {
            try
            {
                if (client.Url.StartsWith("https://streamtape.com/v/"))
                    client.Url = client.Url.Replace("https://streamtape.com/v/", "https://streamtape.com/e/");
                var page = await BrowserAutomation.Browser.NewPageAsync();
                await page.GoToAsync(client.Url);
                await page.WaitForSelectorAsync("button.plyr__control--overlaid");
                await page.MainFrame.EvaluateExpressionAsync("document.querySelector(\".play-overlay\").click()");
                await Task.Delay(100);
                await page.MainFrame.EvaluateExpressionAsync("document.querySelector(\".play-overlay\").click()");
                await Task.Delay(100);
                await page.MainFrame.EvaluateExpressionAsync(
                    "document.querySelector('.plyr__control--overlaid').click()");
                await Task.Delay(100);
                await page.MainFrame.EvaluateExpressionAsync(
                    "document.querySelector('.plyr__controls__item').click()");
                await Task.Delay(100);
                var html = await page.GetContentAsync();
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(html);
                HtmlNode videoNode = doc.DocumentNode.SelectSingleNode("//video");
                var webTitle = client.FileName;
                var downloadUri = "https:" + videoNode?.Attributes["src"]?.Value?.Replace("amp;", "");
                await page.CloseAsync();
                return new DownloadExtract()
                {
                    DownloadLink = downloadUri
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new DownloadExtract()
                {
                    DownloadLink = client.Url
                };
            }
        }
    }
}