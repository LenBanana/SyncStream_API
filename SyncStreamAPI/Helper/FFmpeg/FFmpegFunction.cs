using SyncStreamAPI.PostgresModels;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegFunction : IFFmpegFunction
    {
        public string DefaultConversionRegex = @"video:\d+kB audio:\d+kB subtitle:\d+kB other streams:\d+kB global headers:\d+kB muxing overhead:";
        public string DefaultErrorRegex = @"^Conversion failed!$";
        public DbFile InputFile { get; set; }
        public DbFile OutputFile { get; set; }
        public string TargetFormat => OutputFile.FileEnding;
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public IProgress<double> Progress { get; set; } = null;

        public FFmpegFunction(string inputPath, string outputPath)
        {
            this.InputPath = inputPath;
            this.OutputPath = outputPath;
        }
        public FFmpegFunction(string inputPath, string outputPath, IProgress<double> progress) : this(inputPath, outputPath)
        {
            this.Progress = progress;
        }

        public FFmpegFunction(string inputPath, string outputPath, TimeSpan start, TimeSpan end, IProgress<double> progress) : this(inputPath, outputPath, progress)
        {
            this.Start = start;
            this.End = end;
        }

        public static FFmpegFunction GetDefaultFunction(FileInfo fileInfo, string extension, DbUser dbUser)
        {
            var dbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser);
            var path = Path.Combine(General.TemporaryFilePath, $"{dbfile.FileKey}{dbfile.FileEnding}");
            var outputDbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), extension, dbUser, temporary: true);
            var outputPath = Path.Combine(General.TemporaryFilePath, $"{outputDbfile.FileKey}{extension}");
            var function = new FFmpegFunction(path, outputPath);
            function.InputFile = dbfile;
            function.OutputFile = outputDbfile;
            return function;
        }

        public Task<string> Execute()
        {
            throw new NotImplementedException();
        }
    }
}
