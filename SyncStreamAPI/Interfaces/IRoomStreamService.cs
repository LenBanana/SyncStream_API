using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SyncStreamAPI.Interfaces;

public interface IRoomStreamService
{
    Task<RoomUploadSessionResult> StartRoomUploadAsync(string token, string uniqueId, string name, string fileEnding,
        long totalSize, bool skipPlaylist = false);

    Task<RoomUploadSessionResult> UploadRoomChunkAsync(HttpRequest request, string token, string uploadId,
        long startByte);

    Task<RoomUploadSessionResult> GetRoomUploadStatusAsync(string token, string uploadId);

    Task<RoomUploadSessionResult> CompleteRoomUploadAsync(string token, string uploadId, string scheme,
        HostString host);

    Task<RoomUploadSessionResult> CancelRoomUploadAsync(string token, string uploadId);

    Task<StreamToRoomResult> StreamToRoomAsync(HttpRequest request, string token, string uniqueId, string name,
        string fileEnding);

    Task<RoomUploadPlaybackAssetResult> GetRoomUploadPlaybackAssetAsync(string uploadId);

    HlsSegmentResult GetHlsSegment(string fileKey, string fileName);

    void CleanupRoomUploadArtifacts(string uploadId);
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

public class RoomUploadPlaybackAssetResult
{
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? FilePath { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
}

public class HlsSegmentResult
{
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public FileStream? Stream { get; init; }
    public string? ContentType { get; init; }
    public bool DisableCache { get; init; }
}