using ScreenIT.Helper;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegMediaInfo : FFmpegFunction
    {
        public FFmpegMediaInfo(string inputPath, string outputPath) : base(inputPath, outputPath)
        {
        }

        public FFmpegMediaInfo(string inputPath, string outputPath, IProgress<double> progress) : base(inputPath, outputPath, progress)
        {
        }
        public async Task<double?> GetTotalFrames()
        {
            var frames = 0d;
            try
            {
                var args = $"-i \"{InputPath}\" -f null -";
                if (Start != TimeSpan.Zero && End != TimeSpan.Zero)
                {
                    string formattedStart = $"{(int)Start.TotalHours:D2}:{Start.Minutes:D2}:{Start.Seconds:D2}";
                    string formattedEnd = $"{(int)End.TotalHours:D2}:{End.Minutes:D2}:{End.Seconds:D2}";
                    args = $"-ss {formattedStart} -to {formattedEnd} -i \"{InputPath}\" -f null -";
                }
                var p = new Progress<double>(d =>
                {
                    frames = d;
                });

                bool success = await FFmpegTools.ExecuteFFMPEG(args,
                    exitCondition: e => Regex.IsMatch(e.Data, DefaultConversionRegex),
                    errorCondition: e => Regex.IsMatch(e.Data, DefaultErrorRegex),
                    p
                );
            }
            catch (Exception)
            {
                return null;
            }

            return frames;
        }

    }
}
