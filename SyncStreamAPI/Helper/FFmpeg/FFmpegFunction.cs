using SyncStreamAPI.PostgresModels;
using System;
using System.IO;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public class FFmpegFunction
    {
        public string DefaultConversionRegex = @"video:\d+kB audio:\d+kB subtitle:\d+kB other streams:\d+kB global headers:\d+kB muxing overhead:";
        public string DefaultErrorRegex = @"^Conversion failed!$";

        public FFmpegFunction(string inputPath, string outputPath)
        {
            this.inputPath = inputPath;
            this.outputPath = outputPath;
        }
        public FFmpegFunction(string inputPath, string outputPath, IProgress<double> progress) : this(inputPath, outputPath)
        {
            this.progress = progress;
        }

        public FFmpegFunction(string inputPath, string outputPath, TimeSpan start, TimeSpan end, IProgress<double> progress) : this(inputPath, outputPath, progress)
        {
            this.start = start;
            this.end = end;
        }

        public DbFile inputFile { get; set; }
        public DbFile outputFile { get; set; }
        public string targetFormat => outputFile.FileEnding;
        public string inputPath { get; set; }
        public string outputPath { get; set; }
        public TimeSpan start { get; set; }
        public TimeSpan end { get; set; }
        public IProgress<double> progress { get; set; } = null;

        public static FFmpegFunction GetDefaultFunction(FileInfo fileInfo, string extension, DbUser dbUser)
        {
            var dbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser);
            var path = Path.Combine(General.TemporaryFilePath, $"{dbfile.FileKey}{dbfile.FileEnding}");
            var outputDbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), extension, dbUser, temporary: true);
            outputDbfile.Created = DateTime.UtcNow.AddDays(-General.DaysToKeepImages.Days).AddMinutes(General.MinutesToKeepFFmpeg.Minutes);
            var outputPath = Path.Combine(General.TemporaryFilePath, $"{outputDbfile.FileKey}{extension}");
            var function = new FFmpegFunction(path, outputPath);
            function.inputFile = dbfile;
            function.outputFile = outputDbfile;
            return function;
        }
    }
}
