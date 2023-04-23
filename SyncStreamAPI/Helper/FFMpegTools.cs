using SyncStreamAPI.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace ScreenIT.Helper
{
    public static class FFMpegTools
    {
        public static async Task<string> ConvertToGif(string inputPath, string outputPath)
        {
            FileCheck.CheckOverrideFile(outputPath);
            var outputFolder = Path.GetDirectoryName(outputPath);
            var tempPalettePath = Path.Combine(outputFolder, "temp_palette.png");

            await GeneratePalette(inputPath, tempPalettePath);
            await ConvertToGif(inputPath, tempPalettePath, outputPath);
            await Task.Delay(100);
            if (File.Exists(tempPalettePath))
                File.Delete(tempPalettePath);

            return outputPath;
        }

        private static async Task GeneratePalette(string inputPath, string tempPalettePath)
        {
            var args = $"-y -i \"{inputPath}\" -vf \"fps=10,scale=trunc(iw/2)*2:trunc(ih/2)*2:flags=lanczos,palettegen=stats_mode=diff\" -c:v gif \"{tempPalettePath}\"";
            await ExecuteFFMPEG(args, e => Regex.IsMatch(e.Data, @"video:\d+kB audio:\d+kB subtitle:\d+kB other streams:\d+kB global headers:\d+kB muxing overhead:"));
        }

        private static async Task ConvertToGif(string inputPath, string tempPalettePath, string outputPath)
        {
            var args = $"-i \"{inputPath}\" -i \"{tempPalettePath}\" -lavfi \"fps=10,scale=trunc(iw/2)*2:trunc(ih/2)*2:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -y \"{outputPath}\"";
            await ExecuteFFMPEG(args, e => Regex.IsMatch(e.Data, @"video:\d+kB audio:\d+kB subtitle:\d+kB other streams:\d+kB global headers:\d+kB muxing overhead:"));
        }

        public static async Task<string> ConvertVideo(string inputPath, string outputPath)
        {
            FileCheck.CheckOverrideFile(outputPath);

            var args = $"-i \"{inputPath}\" -c:v h264 -preset slow -crf 18 -pix_fmt yuv420p -c:a copy \"{outputPath}\"";
            var result = await ExecuteFFMPEG(args, e => Regex.IsMatch(e.Data, @"^\[libx264 @ [0-9a-f]+\] kb\/s:\d+(\.\d+)?$"));
            return outputPath;
        }

        public static async Task<string> ConvertAudio(string inputPath, string outputPath, string targetFormat)
        {
            FileCheck.CheckOverrideFile(outputPath);

            var args = $"-i \"{inputPath}\" -c:a {GetAudioCodec(targetFormat)} -b:a 192k \"{outputPath}\"";
            var result = await ExecuteFFMPEG(args, e => Regex.IsMatch(e.Data, @"video:\d+kB audio:\d+kB subtitle:\d+kB other streams:\d+kB global headers:\d+kB muxing overhead:"));
            return outputPath;
        }

        public static async Task<string> ExtractAudio(string inputPath, string outputPath)
        {
            FileCheck.CheckOverrideFile(outputPath);

            var args = $"-i \"{inputPath}\" -vn -acodec libmp3lame \"{outputPath}\"";
            var success = await ExecuteFFMPEG(args, e =>
            {
                var errorPattern = @"^Output file #\d+ does not contain any stream$";
                var pattern = @"video:\d+kB audio:\d+kB subtitle:\d+kB other streams:\d+kB global headers:\d+kB muxing overhead:";
                return Regex.IsMatch(e.Data, pattern) || Regex.IsMatch(e.Data, errorPattern);
            });

            return success && File.Exists(outputPath) ? outputPath : null;
        }

        public static async Task<string> CutMedia(string inputPath, string outputPath, TimeSpan start, TimeSpan end)
        {
            try
            {
                FileCheck.CheckOverrideFile(outputPath);
                var args = $"-i \"{inputPath}\" -ss {start} -to {end} -c copy \"{outputPath}\"";
                var success = await ExecuteFFMPEG(args, e => 
                (Regex.IsMatch(e.Data, @"video:\d+kB audio:\d+kB subtitle:\d+kB other streams:\d+kB global headers:\d+kB muxing overhead:") 
                || Regex.IsMatch(e.Data, @"^-to value smaller than -ss; aborting\.$")));

                return success && File.Exists(outputPath) ? outputPath : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static async Task<bool> ExecuteFFMPEG(string args, Func<DataReceivedEventArgs, bool> exitCondition)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = General.GetFFmpegPath(),
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                var exitSignal = new ManualResetEvent(false);

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        if (exitCondition(e)) exitSignal.Set();
                        Debug.WriteLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.Run(() => exitSignal.WaitOne());
                process.Dispose();
                process = null;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetAudioCodec(string format)
        {
            switch (format.ToLowerInvariant())
            {
                case "mp3":
                    return "libmp3lame";
                case "wav":
                    return "pcm_s16le";
                case "ogg":
                    return "libvorbis";
                case "flac":
                    return "flac";
                case "aiff":
                    return "pcm_s16be";
                case "m4a":
                    return "aac";
                default:
                    throw new ArgumentException($"Unsupported audio format: {format}");
            }
        }
    }
}