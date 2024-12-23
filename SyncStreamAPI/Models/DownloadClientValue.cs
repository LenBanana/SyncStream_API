﻿using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace SyncStreamAPI.Models;

public class DownloadClientValue
{
    private readonly WebClient keepAliveClient = new();
    private bool stopKeepAlive;

    public DownloadClientValue(int userId, string fileName, string token, string url, ConversionPreset preset)
    {
        UserId = userId;
        Preset = preset;
        Token = token;
        FileName = fileName;
        Url = url;
        Running = false;
        UniqueId = Guid.NewGuid().ToString();
        CancellationToken = new CancellationTokenSource();
    }

    public DownloadClientValue(int userId, string fileName, string token, string url, string quality,
        bool audioOnly = false, bool embedSubtitles = false)
    {
        UserId = userId;
        Preset = ConversionPreset.Faster;
        Quality = quality;
        Token = token;
        FileName = fileName;
        Url = url;
        Running = false;
        AudioOnly = audioOnly;
        EmbedSubtitles = embedSubtitles;
        UniqueId = Guid.NewGuid().ToString();
        CancellationToken = new CancellationTokenSource();
    }

    public int UserId { get; set; }
    public ConversionPreset Preset { get; set; }
    public string Quality { get; set; }
    public string FileName { get; set; }
    public string Url { get; set; }
    public string Token { get; set; }
    public string UniqueId { get; set; }
    public bool Running { get; set; }
    public bool AudioOnly { get; set; }
    public bool EmbedSubtitles { get; set; }
    public Stopwatch Stopwatch { get; set; }
    public CancellationTokenSource CancellationToken { get; set; }

    public async void KeepUrlAlive()
    {
        if (Stopwatch == null && !stopKeepAlive)
        {
            try
            {
                await keepAliveClient?.DownloadStringTaskAsync(new Uri(Url));
            }
            catch
            {
            }

            await Task.Delay(1000);
            KeepUrlAlive();
        }

        keepAliveClient?.Dispose();
    }

    public void StopKeepAlive()
    {
        stopKeepAlive = true;
    }
}