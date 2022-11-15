using System;
using Xabe.FFmpeg;

namespace SyncStreamAPI.Models
{
#nullable enable
    public class DownloadFileInfo
    {
        public DownloadFileInfo(string fileName, string fileType, long size, string hash, IVideoStream? videoStream, IAudioStream? audioStream, DateTime creationDate)
        {
            FileName = fileName;
            FileType = fileType;
            Size = size;
            Hash = hash;
            VideoStream = videoStream;
            AudioStream = audioStream;
            CreationDate = creationDate;
        }

        public string FileName { get; set; }
        public string FileType { get; set; }
        public long Size { get; set; }
        public string Hash { get; set; }
        IVideoStream? VideoStream { get; set; }
        IAudioStream? AudioStream { get; set; }
        public long? VideoBitrate => VideoStream?.Bitrate;
        public int? VideoHeight => VideoStream?.Height;
        public int? VideoWidth => VideoStream?.Width;
        public TimeSpan? Duration => VideoStream?.Duration;
        public string? VideoCodec => VideoStream?.Codec;
        public double? VideoFramerate => VideoStream?.Framerate;
        public long? AudioBitrate => AudioStream?.Bitrate;
        public int? AudioChannels => AudioStream?.Channels;
        public string? AudioCodec => AudioStream?.Codec;
        public DateTime CreationDate { get; set; }
    }
}
