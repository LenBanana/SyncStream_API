using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg;

public class FFmpegMediaInfo : FFmpegFunction
{
    public FFmpegMediaInfo(string inputPath, string outputPath) : base(inputPath, outputPath)
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
                var formattedStart = $"{(int)Start.TotalHours:D2}:{Start.Minutes:D2}:{Start.Seconds:D2}";
                var formattedEnd = $"{(int)End.TotalHours:D2}:{End.Minutes:D2}:{End.Seconds:D2}";
                args = $"-ss {formattedStart} -to {formattedEnd} -i \"{InputPath}\" -f null -";
            }

            var p = new Progress<double>(d => { frames = d; });

            var success = await FFmpegTools.ExecuteFfmpeg(args,
                e => Regex.IsMatch(e.Data, DefaultConversionRegex),
                e => Regex.IsMatch(e.Data, DefaultErrorRegex),
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