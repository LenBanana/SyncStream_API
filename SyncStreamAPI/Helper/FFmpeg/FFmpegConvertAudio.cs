using ScreenIT.Helper;
using SyncStreamAPI.PostgresModels;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegConvertAudio : FFmpegFunction, IFFmpegFunction
    {
        public FFmpegConvertAudio(string inputPath, string outputPath, DbFile inputFile = null, DbFile outputFile = null) : base(inputPath, outputPath)
        {
            InputFile = inputFile;
            OutputFile = outputFile;
        }

        public new async Task<string> Execute()
        {
            FileCheck.CheckOverrideFile(OutputPath);

            var args = $"-i \"{InputPath}\" -c:a {FFmpegTools.GetAudioCodec(TargetFormat)} -b:a 192k \"{OutputPath}\"";
            var success = await FFmpegTools.ExecuteFfmpeg(args,
                exitCondition: e => Regex.IsMatch(e.Data, DefaultConversionRegex),
                errorCondition: e => Regex.IsMatch(e.Data, DefaultErrorRegex),
                Progress
                );
            return success && File.Exists(OutputPath) ? OutputPath : null;
        }
    }
}
