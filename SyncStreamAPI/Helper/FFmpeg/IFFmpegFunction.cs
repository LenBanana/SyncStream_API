using SyncStreamAPI.PostgresModels;
using System;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper.FFmpeg
{
    public interface IFFmpegFunction
    {
        public DbFile InputFile { get; set; }
        public DbFile OutputFile { get; set; }
        public string TargetFormat => OutputFile.FileEnding;
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public IProgress<double> Progress { get; set; }
        public Task<string> Execute();
    }
}
