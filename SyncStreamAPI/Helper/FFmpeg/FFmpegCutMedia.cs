﻿using ScreenIT.Helper;
using SyncStreamAPI.PostgresModels;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegCutMedia : FFmpegFunction, IFFmpegFunction
    {
        public FFmpegCutMedia(string inputPath, string outputPath, TimeSpan start, TimeSpan end, IProgress<double> progress, DbFile inputFile = null, DbFile outputFile = null) : base(inputPath, outputPath, start, end, progress)
        {
            InputFile = inputFile;
            OutputFile = outputFile;
        }

        public new async Task<string> Execute()
        {
            try
            {
                FileCheck.CheckOverrideFile(OutputPath);
                string formattedStart = $"{(int)Start.TotalHours:D2}:{Start.Minutes:D2}:{Start.Seconds:D2}";
                string formattedEnd = $"{(int)End.TotalHours:D2}:{End.Minutes:D2}:{End.Seconds:D2}";
                var args = $"-ss {formattedStart} -to {formattedEnd} -i \"{InputPath}\" -c copy \"{OutputPath}\"";

                var success = await FFmpegTools.ExecuteFfmpeg(args,
                exitCondition: e => Regex.IsMatch(e.Data, DefaultConversionRegex),
                errorCondition: e => Regex.IsMatch(e.Data, DefaultErrorRegex) || Regex.IsMatch(e.Data, @"^-to value smaller than -ss; aborting\.$"),
                Progress
                );

                return success && File.Exists(OutputPath) ? OutputPath : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
