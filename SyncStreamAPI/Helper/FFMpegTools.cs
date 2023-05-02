using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Helper.FFmpeg;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models.MediaModels;
using SyncStreamAPI.PostgresModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace ScreenIT.Helper
{
    public static class FFmpegTools
    {
        public static async Task<bool> ExecuteFFMPEG(
            string args,
            Func<DataReceivedEventArgs, bool> exitCondition,
            Func<DataReceivedEventArgs, bool> errorCondition = null,
            IProgress<double> progress = null)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo()
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
                var tcs = new TaskCompletionSource<bool>();
                // Set up the timer to trigger after 10 seconds of no output
                var timer = new Timer(state =>
                {
                    tcs.TrySetResult(false);
                }, null, General.FFmpegTimeout, Timeout.Infinite);

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
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
                                double frame = double.Parse(match.Groups[1].Value);
                                progress.Report(frame);
                            }
                        }
                        Console.WriteLine(e.Data);
                        // Reset the timer if new output is received
                        timer.Change(General.FFmpegTimeout, Timeout.Infinite);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                var success = await tcs.Task;
                process.Kill();
                process.Dispose();
                process = null;

                return success;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<ActionResult> ProcessMedia(
            IFormFile inputFile,
            DbUser dbUser,
            IFFmpegFunction function,
            string mimeType,
            PostgresContext postgresContext,
            IHubContext<ServerHub, IServerHub> serverHub)
        {
            var fileInfo = new FileInfo(inputFile.FileName);
            var editProcess = new EditorProcess();
            editProcess.Text = $"Processing - {fileInfo.Name}...";
            editProcess.AlertType = AlertType.Info;
            await serverHub.Clients.Group(dbUser.ApiKey).mediaStatus(editProcess);
            try
            {
                using (var stream = new FileStream(function.InputPath, FileMode.Create))
                {
                    await inputFile.CopyToAsync(stream);
                }
                var mediaInfo = new FFmpegMediaInfo(function.InputPath, function.OutputPath);
                mediaInfo.Start = function.Start;
                mediaInfo.End = function.End;
                var totalFrames = await mediaInfo.GetTotalFrames();
                var p = new Progress<double>(async d =>
                {
                    editProcess.Progress = totalFrames.HasValue ? d / totalFrames.Value * 100 : 100;
                    await serverHub.Clients.Group(dbUser.ApiKey).mediaStatus(editProcess);
                });
                function.Progress = p;
                string result = await function.Execute();
                if (result != null)
                {
                    editProcess.AlertType = AlertType.Success;
                    editProcess.Text = $"Success - {fileInfo.Name}...";
                    await serverHub.Clients.Group(dbUser.ApiKey).mediaStatus(editProcess);
                    var fileBytes = await File.ReadAllBytesAsync(function.OutputPath);
                    function.OutputFile.DateToBeDeleted = DateTime.UtcNow.AddMinutes(General.MinutesToKeepFFmpeg.Minutes);
                    var savedFile = postgresContext.Files?.Add(function.OutputFile);
                    await postgresContext.SaveChangesAsync();
                    await serverHub.Clients.Group(dbUser.ApiKey).updateFolders(new SyncStreamAPI.DTOModel.FileDto(function.OutputFile));
                    FileContentResult fileResult = new FileContentResult(fileBytes, mimeType);
                    await serverHub.Clients.Group(dbUser.ApiKey).mediaStatus(editProcess);
                    return fileResult;
                }
                else
                {
                    if (File.Exists(function.OutputPath))
                        FileCheck.CheckOverrideFile(function.OutputPath);
                    editProcess.AlertType = AlertType.Danger;
                    editProcess.Text = $"Failed - {fileInfo.Name}...";
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
            switch (format.ToLowerInvariant())
            {
                case "mp3":
                case ".mp3":
                    return "libmp3lame";
                case "wav":
                case ".wav":
                    return "pcm_s16le";
                case "ogg":
                case ".ogg":
                    return "libvorbis";
                case "flac":
                case ".flac":
                    return "flac";
                case "aiff":
                case ".aiff":
                    return "pcm_s16be";
                case "m4a":
                case ".m4a":
                    return "aac";
                case "mp4":
                case "mov":
                case "mkv":
                case ".mp4":
                case ".mov":
                case ".mkv":
                    return "aac";
                case "avi":
                case ".avi":
                    return "mp3";
                case "wmv":
                case ".wmv":
                    return "wmav2";
                default:
                    return "copy";
            }
        }

        public static string GetVideoCodec(string format)
        {
            switch (format.ToLowerInvariant())
            {
                case "avi":
                case ".avi":
                    return "mpeg4";
                case "wmv":
                case ".wmv":
                    return "wmv2";
                case "flv":
                case ".flv":
                    return "flv1";
                case "webm":
                case ".webm":
                    return "libvpx";
                default:
                    return "h264";
            }
        }
    }
}