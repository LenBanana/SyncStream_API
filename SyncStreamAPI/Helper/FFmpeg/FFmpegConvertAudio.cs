using ScreenIT.Helper;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegConvertAudio : FFmpegFunction, IFFmpegFunction
    {
        public FFmpegConvertAudio(string inputPath, string outputPath) : base(inputPath, outputPath)
        {
        }

        public FFmpegConvertAudio(string inputPath, string outputPath, IProgress<double> progress) : base(inputPath, outputPath, progress)
        {
        }

        public new async Task<string> Execute()
        {
            FileCheck.CheckOverrideFile(OutputPath);

            var args = $"-i \"{InputPath}\" -c:a {FFmpegTools.GetAudioCodec(TargetFormat)} -b:a 192k \"{OutputPath}\"";
            var success = await FFmpegTools.ExecuteFFMPEG(args,
                exitCondition: e => Regex.IsMatch(e.Data, DefaultConversionRegex),
                errorCondition: e => Regex.IsMatch(e.Data, DefaultErrorRegex),
                Progress
                );
            return success && File.Exists(OutputPath) ? OutputPath : null;
        }
    }
}
