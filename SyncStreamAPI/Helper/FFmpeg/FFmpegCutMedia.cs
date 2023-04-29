using ScreenIT.Helper;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegCutMedia : FFmpegFunction
    {
        public FFmpegCutMedia(string inputPath, string outputPath) : base(inputPath, outputPath)
        {
        }

        public FFmpegCutMedia(string inputPath, string outputPath, IProgress<double> progress) : base(inputPath, outputPath, progress)
        {
        }

        public FFmpegCutMedia(string inputPath, string outputPath, TimeSpan start, TimeSpan end, IProgress<double> progress) : base(inputPath, outputPath, start, end, progress)
        {
        }

        public async Task<string> CutMedia()
        {
            try
            {
                FileCheck.CheckOverrideFile(outputPath);
                var args = $"-i \"{inputPath}\" -ss {start} -to {end} -c copy \"{outputPath}\"";
                var success = await FFmpegTools.ExecuteFFMPEG(args,
                exitCondition: e => Regex.IsMatch(e.Data, DefaultConversionRegex),
                errorCondition: e => Regex.IsMatch(e.Data, @"^-to value smaller than -ss; aborting\.$"),
                progress
                );

                return success && File.Exists(outputPath) ? outputPath : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
