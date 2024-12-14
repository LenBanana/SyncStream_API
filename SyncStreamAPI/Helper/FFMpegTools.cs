using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ScreenIT.Helper;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper.FFmpeg;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models.MediaModels;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Helper;

public static class FFmpegTools
{
    public static async Task<bool> ExecuteFfmpeg(
        string args,
        Func<DataReceivedEventArgs, bool> exitCondition,
        Func<DataReceivedEventArgs, bool> errorCondition = null,
        IProgress<double> progress = null)
    {
        var success = false;
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = General.GetFFmpegPath(),
                Arguments = "-progress pipe:2 " + args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        try
        {
            var tcs = new TaskCompletionSource<bool>();
            // Set up the timer to trigger after 10 seconds of no output
            var timer = new Timer(state => { tcs.TrySetResult(false); }, null, General.FFmpegTimeout, Timeout.Infinite);

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                if (errorCondition != null && errorCondition(e))
                {
                    tcs.TrySetResult(false);
                }
                else if (exitCondition(e))
                {
                    tcs.TrySetResult(true);
                }
                else if (progress != null)
                {
                    var match = Regex.Match(e.Data, @"frame=\s*(\d+)");
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var frame = double.Parse(match.Groups[1].Value);
                        progress.Report(frame);
                    }
                }

                Console.WriteLine(e.Data);
                // Reset the timer if new output is received
                timer.Change(General.FFmpegTimeout, Timeout.Infinite);
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            success = await tcs.Task;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        process.Kill();
        process.Dispose();
        return success;
    }

    public static async Task<ActionResult> ProcessMedia(
        IFormFile inputFile,
        DbUser dbUser,
        IFFmpegFunction function,
        string mimeType,
        PostgresContext postgresContext,
        IHubContext<ServerHub, IServerHub> serverHub)
    {
        var outputFileName = function.OutputFile.Name;
        var editProcess = new EditorProcess
        {
            Text = $"Processing - {outputFileName}...",
            AlertType = AlertType.Info
        };
        await serverHub.Clients.Group(dbUser.ApiKey).mediaStatus(editProcess);
        try
        {
            await using (var stream = new FileStream(function.InputPath, FileMode.Create))
            {
                await inputFile.CopyToAsync(stream);
            }

            var mediaInfo = new FFmpegMediaInfo(function.InputPath, function.OutputPath)
            {
                Start = function.Start,
                End = function.End
            };
            var totalFrames = await mediaInfo.GetTotalFrames();

            async void Handler(double d)
            {
                editProcess.Progress = totalFrames.HasValue ? d / totalFrames.Value * 100 : 100;
                await serverHub.Clients.Group(dbUser.ApiKey).mediaStatus(editProcess);
            }

            var p = new Progress<double>(Handler);
            function.Progress = p;
            var result = await function.Execute();
            if (result != null)
            {
                editProcess.AlertType = AlertType.Success;
                editProcess.Text = $"Success - {outputFileName}...";
                await serverHub.Clients.Group(dbUser.ApiKey).mediaStatus(editProcess);
                var fileBytes = await File.ReadAllBytesAsync(function.OutputPath);
                function.OutputFile.DateToBeDeleted = DateTime.UtcNow.AddMinutes(General.MinutesToKeepFFmpeg.Minutes);
                var savedFile = postgresContext.Files?.Add(function.OutputFile);
                await postgresContext.SaveChangesAsync();
                await serverHub.Clients.Group(dbUser.ApiKey).updateFolders(new FileDto(function.OutputFile));
                var fileResult = new FileContentResult(fileBytes, mimeType);
                await serverHub.Clients.Group(dbUser.ApiKey).mediaStatus(editProcess);
                return fileResult;
            }
            else
            {
                if (File.Exists(function.OutputPath))
                    FileCheck.CheckOverrideFile(function.OutputPath);
                editProcess.AlertType = AlertType.Danger;
                editProcess.Text = $"Failed - {outputFileName}...";
                await serverHub.Clients.Group(dbUser.ApiKey).mediaStatus(editProcess);
                return new StatusCodeResult(StatusCodes.Status404NotFound);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
        finally
        {
            // Wait some time before to give the user time to read the message
            FileCheck.CheckOverrideFile(function.InputPath);
            await Task.Delay(2500);
            await serverHub.Clients.Group(dbUser.ApiKey).finishStatus(editProcess);
        }
    }

    public static string GetAudioCodec(string format)
    {
        var mediaType = ParseMediaType(format);
        switch (mediaType)
        {
            case MediaType.MP3:
                return "libmp3lame";
            case MediaType.WAV:
                return "pcm_s16le";
            case MediaType.OGG:
                return "libvorbis";
            case MediaType.FLAC:
                return "flac";
            case MediaType.AIFF:
                return "pcm_s16be";
            case MediaType.M4A:
            case MediaType.MP4:
            case MediaType.MOV:
            case MediaType.MKV:
                return "aac";
            case MediaType.AVI:
                return "mp3";
            case MediaType.WMV:
                return "wmav2";
            case MediaType.FLV:
            case MediaType.WEBM:
                return "vorbis";
            case MediaType.PNG:
            case MediaType.JPEG:
            case MediaType.BMP:
            case MediaType.TIFF:
            case MediaType.WEBP:
            case MediaType.PDF:
            case MediaType.GIF:
            default:
                return "copy";
        }
    }

    public static string GetVideoCodec(string format)
    {
        var mediaType = ParseMediaType(format);
        return mediaType switch
        {
            MediaType.AVI => "mpeg4",
            MediaType.WMV => "wmv2",
            MediaType.FLV => "flv1",
            MediaType.WEBM => "libvpx",
            _ => "h264"
        };
    }

    private static MediaType ParseMediaType(string format)
    {
        format = format.TrimStart('.');
        return Enum.TryParse<MediaType>(format, true, out var mediaType) ? mediaType : MediaType.PNG;
    }
}