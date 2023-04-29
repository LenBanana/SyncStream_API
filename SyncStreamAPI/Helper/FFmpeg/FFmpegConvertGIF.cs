using ScreenIT.Helper;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegConvertGIF : FFmpegFunction
    {
        public FFmpegConvertGIF(string inputPath, string outputPath) : base(inputPath, outputPath)
        {
        }

        public FFmpegConvertGIF(string inputPath, string outputPath, IProgress<double> progress) : base(inputPath, outputPath, progress)
        {
        }

        public async Task<string> ConvertToGif()
        {
            FileCheck.CheckOverrideFile(outputPath);
            var outputFolder = Path.GetDirectoryName(outputPath);
            var tempPalettePath = Path.Combine(outputFolder, "temp_palette.png");
            var success = false;
            success = await GeneratePalette(inputPath, tempPalettePath);
            if (!success)
                return null;
            success = await ConvertToGif(inputPath, tempPalettePath, outputPath);
            await Task.Delay(100);
            if (File.Exists(tempPalettePath))
                File.Delete(tempPalettePath);

            return success && File.Exists(outputPath) ? outputPath : null;
        }

        private async Task<bool> GeneratePalette(string inputPath, string tempPalettePath)
        {
            var args = $"-y -i \"{inputPath}\" -vf \"fps=10,scale=trunc(iw/2)*2:trunc(ih/2)*2:flags=lanczos,palettegen=stats_mode=diff\" -c:v gif \"{tempPalettePath}\"";
            return await FFmpegTools.ExecuteFFMPEG(args, e => Regex.IsMatch(e.Data, DefaultConversionRegex));
        }

        private async Task<bool> ConvertToGif(string inputPath, string tempPalettePath, string outputPath)
        {
            var args = $"-i \"{inputPath}\" -i \"{tempPalettePath}\" -lavfi \"fps=10,scale=trunc(iw/2)*2:trunc(ih/2)*2:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -y \"{outputPath}\"";
            return await FFmpegTools.ExecuteFFMPEG(args,
                exitCondition: e => Regex.IsMatch(e.Data, DefaultConversionRegex),
                errorCondition: e => Regex.IsMatch(e.Data, DefaultErrorRegex)
                );
        }
    }
}
