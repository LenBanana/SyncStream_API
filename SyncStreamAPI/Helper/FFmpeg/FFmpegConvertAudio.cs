using ScreenIT.Helper;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegConvertAudio : FFmpegFunction
    {
        public FFmpegConvertAudio(string inputPath, string outputPath) : base(inputPath, outputPath)
        {
        }

        public FFmpegConvertAudio(string inputPath, string outputPath, IProgress<double> progress) : base(inputPath, outputPath, progress)
        {
        }

        public async Task<string> ConvertAudio()
        {
            FileCheck.CheckOverrideFile(outputPath);

            var args = $"-i \"{inputPath}\" -c:a {FFmpegTools.GetAudioCodec(targetFormat)} -b:a 192k \"{outputPath}\"";
            var success = await FFmpegTools.ExecuteFFMPEG(args,
                exitCondition: e => Regex.IsMatch(e.Data, DefaultConversionRegex),
                errorCondition: e => Regex.IsMatch(e.Data, DefaultErrorRegex),
                progress
                );
            return success && File.Exists(outputPath) ? outputPath : null;
        }
    }
}
