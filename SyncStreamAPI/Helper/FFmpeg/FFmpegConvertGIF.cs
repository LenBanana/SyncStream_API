using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ScreenIT.Helper;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Helper.FFmpeg;

public class FFmpegConvertGIF : FFmpegFunction, IFFmpegFunction
{
    public FFmpegConvertGIF(string inputPath, string outputPath, DbFile inputFile = null, DbFile outputFile = null) :
        base(inputPath, outputPath)
    {
        InputFile = inputFile;
        OutputFile = outputFile;
    }

    public new async Task<string> Execute()
    {
        FileCheck.CheckOverrideFile(OutputPath);
        var outputFolder = Path.GetDirectoryName(OutputPath);
        var tempPalettePath = Path.Combine(outputFolder, "temp_palette.png");
        var success = false;
        success = await GeneratePalette(InputPath, tempPalettePath);
        if (!success)
            return null;
        success = await ConvertToGif(InputPath, tempPalettePath, OutputPath);
        await Task.Delay(100);
        if (File.Exists(tempPalettePath))
            File.Delete(tempPalettePath);

        return success && File.Exists(OutputPath) ? OutputPath : null;
    }

    private async Task<bool> GeneratePalette(string inputPath, string tempPalettePath)
    {
        var args =
            $"-y -i \"{inputPath}\" -vf \"fps=10,scale=trunc(iw/2)*2:trunc(ih/2)*2:flags=lanczos,palettegen=stats_mode=diff\" -c:v gif \"{tempPalettePath}\"";
        return await FFmpegTools.ExecuteFfmpeg(args, e => Regex.IsMatch(e.Data, DefaultConversionRegex));
    }

    private async Task<bool> ConvertToGif(string inputPath, string tempPalettePath, string outputPath)
    {
        var args =
            $"-i \"{inputPath}\" -i \"{tempPalettePath}\" -lavfi \"fps=10,scale=trunc(iw/2)*2:trunc(ih/2)*2:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -y \"{outputPath}\"";
        return await FFmpegTools.ExecuteFfmpeg(args,
            e => Regex.IsMatch(e.Data, DefaultConversionRegex),
            e => Regex.IsMatch(e.Data, DefaultErrorRegex)
        );
    }
}