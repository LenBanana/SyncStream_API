﻿using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace SyncStreamAPI.ServerData.Helper
{
    public class YtDLPHelper
    {
        public static async Task UpdateDownloadProgress(DownloadClientValue downloadClient, IHubContext<ServerHub, IServerHub> _hub, DownloadProgress p)
        {
            try
            {
                var perc = Math.Round(p.Progress * 100f, 2);
                var text = $"{Math.Round(perc, 0)}% {p.DownloadSpeed} - {StopwatchCalc.CalculateRemainingTime(downloadClient.Stopwatch, perc)} remaining";
                var result = new DownloadInfo(text, downloadClient.FileName, downloadClient.UniqueId) { Progress = perc };
                await _hub.Clients.Group(downloadClient.UserId.ToString()).downloadProgress(result);
                if (p.State == DownloadState.Success)
                {
                    await _hub.Clients.Group(downloadClient.UserId.ToString()).downloadFinished(downloadClient.UniqueId);
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
        }

        public static async Task<RunResult<string>> DownloadMedia(YoutubeDL ytdl, DownloadClientValue downloadClient, bool audioOnly, IProgress<DownloadProgress> progress, CancellationToken? cancellationToken = null)
        {
            return audioOnly
                ? await ytdl.RunAudioDownload(downloadClient.Url, AudioConversionFormat.Mp3, progress: progress, ct: cancellationToken.HasValue ? cancellationToken.Value : downloadClient.CancellationToken.Token, overrideOptions: new OptionSet() { AudioMultistreams = false })
                : await ytdl.RunVideoDownload(downloadClient.Url, format: $"bestvideo[height<={downloadClient.Quality}]+bestaudio/best", progress: progress, ct: cancellationToken.HasValue ? cancellationToken.Value : downloadClient.CancellationToken.Token, recodeFormat: VideoRecodeFormat.Mp4, mergeFormat: DownloadMergeFormat.Mp4);
        }
    }
}
