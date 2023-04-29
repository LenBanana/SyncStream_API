using ScreenIT.Helper;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegConvertVideo : FFmpegFunction
    {
        public FFmpegConvertVideo(string inputPath, string outputPath) : base(inputPath, outputPath)
        {
        }

        public FFmpegConvertVideo(string inputPath, string outputPath, IProgress<double> progress) : base(inputPath, outputPath, progress)
        {
        }

        public async Task<string> ConvertVideo()
        {
            FileCheck.CheckOverrideFile(outputPath);
            var outputExtension = Path.GetExtension(outputPath).ToLower();

            string codec = "h264";
            string audioCodec = "copy";
            switch (outputExtension)
            {
                case ".mp4":
                case ".mov":
                case ".mkv":
                    codec = "h264";
                    audioCodec = "aac";
                    break;
                case ".avi":
                    codec = "mpeg4";
                    audioCodec = "mp3";
                    break;
                case ".wmv":
                    codec = "wmv2";
                    audioCodec = "wmav2";
                    break;
            }
            var args = $"-i \"{inputPath}\" -c:v {codec} -preset slow -crf 18 -pix_fmt yuv420p -c:a {audioCodec} \"{outputPath}\"";
            var success = await FFmpegTools.ExecuteFFMPEG(args,
                exitCondition: e => Regex.IsMatch(e.Data, @"^\[libx264 @ [0-9a-f]+\] kb\/s:\d+(\.\d+)?$"),
                errorCondition: e => Regex.IsMatch(e.Data, DefaultErrorRegex),
                progress
            );
            return success && File.Exists(outputPath) ? outputPath : null;
        }
    }
}
