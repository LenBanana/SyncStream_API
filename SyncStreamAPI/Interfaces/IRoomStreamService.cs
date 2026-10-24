using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SyncStreamAPI.Interfaces;

public interface IRoomStreamService
{
    Task<RoomUploadSessionResult> StartRoomUploadAsync(int? userId, string uniqueId, string name, string fileEnding,
        long totalSize, bool skipPlaylist = false);

    Task<RoomUploadSessionResult> UploadRoomChunkAsync(HttpRequest request, int? userId, string uploadId,
        long startByte);

    Task<RoomUploadSessionResult> GetRoomUploadStatusAsync(int? userId, string uploadId);

    Task<RoomUploadSessionResult> CompleteRoomUploadAsync(int? userId, string uploadId, string publicBaseUrl);

    Task<RoomUploadSessionResult> CancelRoomUploadAsync(int? userId, string uploadId);

    Task<StreamToRoomResult> StreamToRoomAsync(HttpRequest request, int? userId, string uniqueId, string name,
        string fileEnding);

    HlsSegmentResult GetHlsSegment(string fileKey, string fileName);
}

public class RoomUploadSessionResult
{
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? UploadId { get; init; }
    public string? State { get; init; }
    public int ChunkSize { get; init; }
    public long NextOffset { get; init; }
    public long TotalSize { get; init; }
    public string? FileKey { get; init; }
    public string? PlaybackUrl { get; init; }
}

public class StreamToRoomResult
{
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? FileKey { get; init; }
}

public class HlsSegmentResult
{
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public FileStream? Stream { get; init; }
    public string? ContentType { get; init; }
    public bool DisableCache { get; init; }
}