using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ScreenIT.Helper;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Helper.FFmpeg;

public class FFmpegExtractAudio : FFmpegFunction, IFFmpegFunction
{
    public FFmpegExtractAudio(string inputPath, string outputPath, DbFile inputFile = null, DbFile outputFile = null) :
        base(inputPath, outputPath)
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
            e => Regex.IsMatch(e.Data, DefaultConversionRegex),
            e => Regex.IsMatch(e.Data, DefaultErrorRegex) ||
                 Regex.IsMatch(e.Data, @"^Output file #\d+ does not contain any stream$")
        );

        return success && File.Exists(OutputPath) ? OutputPath : null;
    }
}