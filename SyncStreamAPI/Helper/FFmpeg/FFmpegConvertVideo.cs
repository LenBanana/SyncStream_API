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
            FileCheck.CheckOverrideFile(OutputPath);
            var outputExtension = Path.GetExtension(OutputPath).ToLower();

            var codec = FFmpegTools.GetVideoCodec(outputExtension);
            var audioCodec = FFmpegTools.GetAudioCodec(outputExtension);
            var args = $"-i \"{InputPath}\" -c:v {codec} -preset slow -crf 18 -pix_fmt yuv420p -c:a {audioCodec} \"{OutputPath}\"";
            var success = await FFmpegTools.ExecuteFFMPEG(args,
                exitCondition: e => Regex.IsMatch(e.Data, @"^\[libx264 @ [0-9a-f]+\] kb\/s:\d+(\.\d+)?$"),
                errorCondition: e => Regex.IsMatch(e.Data, DefaultErrorRegex),
                Progress
            );
            return success && File.Exists(OutputPath) ? OutputPath : null;
        }
    }
}
