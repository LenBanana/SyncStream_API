using ScreenIT.Helper;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegExtractAudio : FFmpegFunction
    {
        public FFmpegExtractAudio(string inputPath, string outputPath) : base(inputPath, outputPath)
        {
        }

        public FFmpegExtractAudio(string inputPath, string outputPath, IProgress<double> progress) : base(inputPath, outputPath, progress)
        {
        }

        public async Task<string> ExtractAudio()
        {
            FileCheck.CheckOverrideFile(outputPath);

            var args = $"-i \"{inputPath}\" -vn -acodec libmp3lame \"{outputPath}\"";
            var success = await FFmpegTools.ExecuteFFMPEG(
                args,
                exitCondition: e => Regex.IsMatch(e.Data, DefaultConversionRegex),
                errorCondition: e => Regex.IsMatch(e.Data, @"^Output file #\d+ does not contain any stream$")
                );

            return success && File.Exists(outputPath) ? outputPath : null;
        }
    }
}
