using System;
using System.IO;
using System.Threading.Tasks;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Helper.FFmpeg;

public class FFmpegFunction : IFFmpegFunction
{
    public string DefaultConversionRegex =
        @"video:\d+kB audio:\d+kB subtitle:\d+kB other streams:\d+kB global headers:\d+kB muxing overhead:";

    public string DefaultErrorRegex = @"^Conversion failed!$";

    public FFmpegFunction(string inputPath, string outputPath)
    {
        InputPath = inputPath;
        OutputPath = outputPath;
    }

    public FFmpegFunction(string inputPath, string outputPath, IProgress<double> progress) : this(inputPath, outputPath)
    {
        Progress = progress;
    }

    public FFmpegFunction(string inputPath, string outputPath, TimeSpan start, TimeSpan end, IProgress<double> progress)
        : this(inputPath, outputPath, progress)
    {
        Start = start;
        End = end;
    }

    public DbFile InputFile { get; set; }
    public DbFile OutputFile { get; set; }
    public string TargetFormat => OutputFile.FileEnding;
    public string InputPath { get; set; }
    public string OutputPath { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public IProgress<double> Progress { get; set; }

    public Task<string> Execute()
    {
        // To implement by the child class
        return Task.FromResult("");
    }

    public static FFmpegFunction GetDefaultFunction(FileInfo fileInfo, string extension, DbUser dbUser)
    {
        var dbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser);
        var path = Path.Combine(General.TemporaryFilePath, $"{dbfile.FileKey}{dbfile.FileEnding}");
        var outputDbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), extension, dbUser, true);
        var outputPath = Path.Combine(General.TemporaryFilePath, $"{outputDbfile.FileKey}{extension}");
        var function = new FFmpegFunction(path, outputPath)
        {
            InputFile = dbfile,
            OutputFile = outputDbfile
        };
        return function;
    }
}