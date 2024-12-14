using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ScreenIT.Helper;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Helper.FFmpeg;

public class FFmpegConvertVideo : FFmpegFunction, IFFmpegFunction
{
    public FFmpegConvertVideo(string inputPath, string outputPath, DbFile inputFile = null, DbFile outputFile = null) :
        base(inputPath, outputPath)
    {
        InputFile = inputFile;
        OutputFile = outputFile;
    }

    public new async Task<string> Execute()
    {
        FileCheck.CheckOverrideFile(OutputPath);
        var outputExtension = Path.GetExtension(OutputPath).ToLower();

        var codec = FFmpegTools.GetVideoCodec(outputExtension);
        var audioCodec = FFmpegTools.GetAudioCodec(outputExtension);
        var args =
            $"-i \"{InputPath}\" -c:v {codec} -preset slow -crf 18 -pix_fmt yuv420p -c:a {audioCodec} \"{OutputPath}\"";
        var success = await FFmpegTools.ExecuteFfmpeg(args,
            e => Regex.IsMatch(e.Data, DefaultConversionRegex) ||
                 Regex.IsMatch(e.Data, @"^\[libx264 @ [0-9a-f]+\] kb\/s:\d+(\.\d+)?$"),
            e => Regex.IsMatch(e.Data, DefaultErrorRegex),
            Progress
        );
        return success && File.Exists(OutputPath) ? OutputPath : null;
    }
}