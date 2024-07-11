using ScreenIT.Helper;
using SyncStreamAPI.PostgresModels;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegExtractAudio : FFmpegFunction, IFFmpegFunction
    {
        public FFmpegExtractAudio(string inputPath, string outputPath, DbFile inputFile = null, DbFile outputFile = null) : base(inputPath, outputPath)
        {
            InputFile = inputFile;
            OutputFile = outputFile;
        }

        public new async Task<string> Execute()
        {
            FileCheck.CheckOverrideFile(OutputPath);

            var args = $"-i \"{InputPath}\" -vn -acodec libmp3lame \"{OutputPath}\"";
            var success = await FFmpegTools.ExecuteFfmpeg(
                args,
                exitCondition: e => Regex.IsMatch(e.Data, DefaultConversionRegex),
                errorCondition: e => Regex.IsMatch(e.Data, DefaultErrorRegex) || Regex.IsMatch(e.Data, @"^Output file #\d+ does not contain any stream$")
                );

            return success && File.Exists(OutputPath) ? OutputPath : null;
        }
    }
}
