using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ScreenIT.Helper;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Helper.FFmpeg;

public class FFmpegConvertAudio : FFmpegFunction, IFFmpegFunction
{
    public FFmpegConvertAudio(string inputPath, string outputPath, DbFile inputFile = null, DbFile outputFile = null) :
        base(inputPath, outputPath)
    {
        InputFile = inputFile;
        OutputFile = outputFile;
    }

    public new async Task<string> Execute()
    {
        FileCheck.CheckOverrideFile(OutputPath);

        var args = $"-i \"{InputPath}\" -c:a {FFmpegTools.GetAudioCodec(TargetFormat)} -b:a 192k \"{OutputPath}\"";
        var success = await FFmpegTools.ExecuteFfmpeg(args,
            e => Regex.IsMatch(e.Data, DefaultConversionRegex),
            e => Regex.IsMatch(e.Data, DefaultErrorRegex),
            Progress
        );
        return success && File.Exists(OutputPath) ? OutputPath : null;
    }
}