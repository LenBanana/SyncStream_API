using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Helper;

namespace SyncStreamAPI.Helper.Streaming;

public class RoomStreamService : IRoomStreamService
{
    private const int DefaultRoomUploadChunkSize = 8 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UploadLocks = new();
    private static readonly HashSet<string> DirectMp4VideoCodecs = new(StringComparer.OrdinalIgnoreCase) { "h264" };
    private static readonly HashSet<string> DirectMp4AudioCodecs = new(StringComparer.OrdinalIgnoreCase) { "aac", "mp3", "mp2" };
    private static readonly HashSet<string> DirectWebmVideoCodecs = new(StringComparer.OrdinalIgnoreCase) { "vp8", "vp9", "av1" };
    private static readonly HashSet<string> DirectWebmAudioCodecs = new(StringComparer.OrdinalIgnoreCase) { "opus", "vorbis" };
    private static readonly HashSet<string> DirectMp3AudioCodecs = new(StringComparer.OrdinalIgnoreCase) { "mp3" };
    private static readonly HashSet<string> DirectAacAudioCodecs = new(StringComparer.OrdinalIgnoreCase) { "aac" };
    private static readonly HashSet<string> DirectOggAudioCodecs = new(StringComparer.OrdinalIgnoreCase) { "opus", "vorbis" };
    private static readonly HashSet<string> DirectWavAudioCodecs = new(StringComparer.OrdinalIgnoreCase)
        { "pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_u8", "pcm_f32le", "pcm_f64le" };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IContentTypeProvider _contentTypeProvider;
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly PostgresContext _postgres;
    private readonly RtmpFileShareManager _rtmpFileShareManager;
    private readonly IServiceScopeFactory _scopeFactory;

    public RoomStreamService(
        IHubContext<ServerHub, IServerHub> hub,
        PostgresContext postgres,
        IContentTypeProvider contentTypeProvider,
        RtmpFileShareManager rtmpFileShareManager,
        IServiceScopeFactory scopeFactory)
    {
        _hub = hub;
        _postgres = postgres;
        _contentTypeProvider = contentTypeProvider;
        _rtmpFileShareManager = rtmpFileShareManager;
        _scopeFactory = scopeFactory;
    }

    public async Task<RoomUploadSessionResult> StartRoomUploadAsync(
        string token,
        string uniqueId,
        string name,
        string fileEnding,
        long totalSize,
        bool skipPlaylist = false)
    {
        try
        {
            var dbUser = await GetUserByTokenAsync(token);
            if (dbUser == null)
                return new RoomUploadSessionResult { StatusCode = StatusCodes.Status401Unauthorized };

            if (RoomManager.GetRoom(uniqueId) == null)
            {
                return new RoomUploadSessionResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ErrorMessage = "Room not found"
                };
            }

            if (totalSize <= 0)
            {
                return new RoomUploadSessionResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ErrorMessage = "File size must be greater than zero"
                };
            }

            var normalizedExtension = skipPlaylist
                ? NormalizeExtensionForServerShare(fileEnding)
                : NormalizeAndValidateExtension(fileEnding);
            if (normalizedExtension == null)
            {
                return new RoomUploadSessionResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ErrorMessage = "Only audio and video files can be uploaded to a room"
                };
            }

            var safeName = Path.GetFileNameWithoutExtension(name);
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "stream";

            var uploadId = Guid.NewGuid().ToString("N");
            var session = new RoomUploadSessionState
            {
                UploadId = uploadId,
                UserId = dbUser.ID,
                UserApiKey = dbUser.ApiKey,
                Username = dbUser.username,
                UniqueId = uniqueId,
                FileName = safeName,
                FileEnding = normalizedExtension,
                TotalSize = totalSize,
                State = RoomUploadStates.Uploading,
                SkipPlaylist = skipPlaylist,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            Directory.CreateDirectory(GetUploadDirectory(uploadId));
            await SaveSessionAsync(session);

            return ToRoomUploadResult(session, 0, StatusCodes.Status200OK);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return new RoomUploadSessionResult
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                ErrorMessage = "Could not start the room upload"
            };
        }
    }

    public async Task<RoomUploadSessionResult> UploadRoomChunkAsync(
        HttpRequest request,
        string token,
        string uploadId,
        long startByte)
    {
        var uploadLock = GetUploadLock(uploadId);
        await uploadLock.WaitAsync(request.HttpContext.RequestAborted);

        try
        {
            var dbUser = await GetUserByTokenAsync(token);
            if (dbUser == null)
                return new RoomUploadSessionResult { StatusCode = StatusCodes.Status401Unauthorized };

            var session = await LoadSessionAsync(uploadId);
            if (session == null || session.UserId != dbUser.ID)
                return new RoomUploadSessionResult { StatusCode = StatusCodes.Status404NotFound };

            if (session.State != RoomUploadStates.Uploading)
                return ToRoomUploadResult(session, GetUploadedBytes(uploadId), StatusCodes.Status409Conflict);

            var currentLength = GetUploadedBytes(uploadId);
            if (startByte != currentLength)
            {
                return new RoomUploadSessionResult
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    ErrorMessage = $"Chunk must start at byte {currentLength}",
                    UploadId = session.UploadId,
                    State = session.State,
                    ChunkSize = DefaultRoomUploadChunkSize,
                    NextOffset = currentLength,
                    TotalSize = session.TotalSize,
                    FileKey = session.FileKey,
                    PlaybackUrl = session.PlaybackUrl
                };
            }

            await using var fileStream = new FileStream(
                GetUploadDataPath(uploadId),
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.ReadWrite,
                65536,
                true);
            fileStream.Seek(startByte, SeekOrigin.Begin);

            var totalWritten = currentLength;
            var bytesWritten = 0L;
            var buffer = new byte[65536];
            int bytesRead;
            while ((bytesRead = await request.Body.ReadAsync(buffer, 0, buffer.Length, request.HttpContext.RequestAborted)) > 0)
            {
                if (totalWritten + bytesRead > session.TotalSize)
                {
                    return new RoomUploadSessionResult
                    {
                        StatusCode = StatusCodes.Status400BadRequest,
                        ErrorMessage = "The uploaded chunk exceeds the declared file size",
                        UploadId = session.UploadId,
                        State = session.State,
                        ChunkSize = DefaultRoomUploadChunkSize,
                        NextOffset = currentLength,
                        TotalSize = session.TotalSize
                    };
                }

                await fileStream.WriteAsync(buffer, 0, bytesRead, request.HttpContext.RequestAborted);
                totalWritten += bytesRead;
                bytesWritten += bytesRead;
            }

            if (bytesWritten <= 0)
            {
                return new RoomUploadSessionResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ErrorMessage = "Chunk body is empty",
                    UploadId = session.UploadId,
                    State = session.State,
                    ChunkSize = DefaultRoomUploadChunkSize,
                    NextOffset = currentLength,
                    TotalSize = session.TotalSize
                };
            }

            session.UpdatedUtc = DateTime.UtcNow;
            await SaveSessionAsync(session);
            return ToRoomUploadResult(session, totalWritten, StatusCodes.Status200OK);
        }
        catch (OperationCanceledException)
        {
            return new RoomUploadSessionResult
            {
                StatusCode = 499,
                ErrorMessage = "Client closed the connection"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return new RoomUploadSessionResult
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                ErrorMessage = "Could not upload the chunk"
            };
        }
        finally
        {
            uploadLock.Release();
        }
    }

    public async Task<RoomUploadSessionResult> GetRoomUploadStatusAsync(string token, string uploadId)
    {
        var uploadLock = GetUploadLock(uploadId);
        await uploadLock.WaitAsync();

        try
        {
            var dbUser = await GetUserByTokenAsync(token);
            if (dbUser == null)
                return new RoomUploadSessionResult { StatusCode = StatusCodes.Status401Unauthorized };

            var session = await LoadSessionAsync(uploadId);
            if (session == null || session.UserId != dbUser.ID)
                return new RoomUploadSessionResult { StatusCode = StatusCodes.Status404NotFound };

            return ToRoomUploadResult(session, GetUploadedBytes(uploadId), StatusCodes.Status200OK);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return new RoomUploadSessionResult
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                ErrorMessage = "Could not read the upload status"
            };
        }
        finally
        {
            uploadLock.Release();
        }
    }

    public async Task<RoomUploadSessionResult> CompleteRoomUploadAsync(
        string token,
        string uploadId,
        string scheme,
        HostString host)
    {
        var uploadLock = GetUploadLock(uploadId);
        await uploadLock.WaitAsync();

        try
        {
            var dbUser = await GetUserByTokenAsync(token);
            if (dbUser == null)
                return new RoomUploadSessionResult { StatusCode = StatusCodes.Status401Unauthorized };

            var session = await LoadSessionAsync(uploadId);
            if (session == null || session.UserId != dbUser.ID)
                return new RoomUploadSessionResult { StatusCode = StatusCodes.Status404NotFound };

            var uploadedBytes = GetUploadedBytes(uploadId);
            if (uploadedBytes != session.TotalSize)
            {
                return new RoomUploadSessionResult
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    ErrorMessage = $"Upload incomplete. Expected {session.TotalSize} bytes but received {uploadedBytes}",
                    UploadId = session.UploadId,
                    State = session.State,
                    ChunkSize = DefaultRoomUploadChunkSize,
                    NextOffset = uploadedBytes,
                    TotalSize = session.TotalSize
                };
            }

            if (session.State == RoomUploadStates.Ready || session.State == RoomUploadStates.Failed)
                return ToRoomUploadResult(session, uploadedBytes, StatusCodes.Status200OK);

            if (session.State == RoomUploadStates.Processing)
                return ToRoomUploadResult(session, uploadedBytes, StatusCodes.Status202Accepted);

            if (session.State != RoomUploadStates.Uploading)
                return ToRoomUploadResult(session, uploadedBytes, StatusCodes.Status409Conflict);

            session.State = RoomUploadStates.Processing;
            session.BaseUrl = $"{scheme}://{host}";
            session.UpdatedUtc = DateTime.UtcNow;
            await SaveSessionAsync(session);

            _ = Task.Run(() => FinalizeRoomUploadAsync(uploadId));
            return ToRoomUploadResult(session, uploadedBytes, StatusCodes.Status202Accepted);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return new RoomUploadSessionResult
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                ErrorMessage = "Could not finalize the upload"
            };
        }
        finally
        {
            uploadLock.Release();
        }
    }

    public async Task<RoomUploadSessionResult> CancelRoomUploadAsync(string token, string uploadId)
    {
        var uploadLock = GetUploadLock(uploadId);
        await uploadLock.WaitAsync();

        try
        {
            var dbUser = await GetUserByTokenAsync(token);
            if (dbUser == null)
                return new RoomUploadSessionResult { StatusCode = StatusCodes.Status401Unauthorized };

            var session = await LoadSessionAsync(uploadId);
            if (session == null || session.UserId != dbUser.ID)
                return new RoomUploadSessionResult { StatusCode = StatusCodes.Status404NotFound };

            session.State = RoomUploadStates.Cancelled;
            session.UpdatedUtc = DateTime.UtcNow;
            await SaveSessionAsync(session);

            TryDeleteDirectory(GetUploadDirectory(uploadId));
            return ToRoomUploadResult(session, 0, StatusCodes.Status200OK);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return new RoomUploadSessionResult
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                ErrorMessage = "Could not cancel the upload"
            };
        }
        finally
        {
            uploadLock.Release();
        }
    }

    public async Task<RoomUploadPlaybackAssetResult> GetRoomUploadPlaybackAssetAsync(string uploadId)
    {
        try
        {
            var session = await LoadSessionAsync(uploadId);
            if (session == null)
            {
                return new RoomUploadPlaybackAssetResult
                {
                    StatusCode = StatusCodes.Status404NotFound,
                    ErrorMessage = "Upload session not found"
                };
            }

            if (session.State != RoomUploadStates.Ready)
            {
                return new RoomUploadPlaybackAssetResult
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    ErrorMessage = "Upload is not ready for direct playback"
                };
            }

            var filePath = GetUploadDataPath(uploadId);
            if (!System.IO.File.Exists(filePath))
            {
                return new RoomUploadPlaybackAssetResult
                {
                    StatusCode = StatusCodes.Status404NotFound,
                    ErrorMessage = "Upload media file not found"
                };
            }

            if (!_contentTypeProvider.TryGetContentType($"file{session.FileEnding}", out var contentType))
                contentType = "application/octet-stream";

            return new RoomUploadPlaybackAssetResult
            {
                StatusCode = StatusCodes.Status200OK,
                FilePath = filePath,
                FileName = session.FileName.EndsWith(session.FileEnding, StringComparison.OrdinalIgnoreCase)
                    ? session.FileName
                    : session.FileName + session.FileEnding,
                ContentType = contentType
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return new RoomUploadPlaybackAssetResult
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                ErrorMessage = "Could not read the completed upload"
            };
        }
    }

    public async Task<StreamToRoomResult> StreamToRoomAsync(
        HttpRequest request,
        string token,
        string uniqueId,
        string name,
        string fileEnding)
    {
        try
        {
            if (request.ContentLength == null || request.ContentLength <= 0)
            {
                return new StreamToRoomResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ErrorMessage = "Request body is empty"
                };
            }

            var startResult = await StartRoomUploadAsync(token, uniqueId, name, fileEnding, request.ContentLength.Value);
            if (startResult.StatusCode != StatusCodes.Status200OK || string.IsNullOrWhiteSpace(startResult.UploadId))
            {
                return new StreamToRoomResult
                {
                    StatusCode = startResult.StatusCode,
                    ErrorMessage = startResult.ErrorMessage
                };
            }

            var chunkResult = await UploadRoomChunkAsync(request, token, startResult.UploadId, 0);
            if (chunkResult.StatusCode != StatusCodes.Status200OK)
            {
                await CancelRoomUploadAsync(token, startResult.UploadId);
                return new StreamToRoomResult
                {
                    StatusCode = chunkResult.StatusCode,
                    ErrorMessage = chunkResult.ErrorMessage
                };
            }

            var completeResult = await CompleteRoomUploadAsync(token, startResult.UploadId, request.Scheme, request.Host);
            return new StreamToRoomResult
            {
                StatusCode = completeResult.StatusCode,
                ErrorMessage = completeResult.ErrorMessage,
                FileKey = completeResult.FileKey
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return new StreamToRoomResult
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                ErrorMessage = "An error occurred while streaming the file to the room"
            };
        }
    }

    public HlsSegmentResult GetHlsSegment(string fileKey, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileKey) ||
            fileKey.Contains('/') || fileKey.Contains('\\') || fileKey.Contains('.'))
            return new HlsSegmentResult { StatusCode = StatusCodes.Status400BadRequest };

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            return new HlsSegmentResult { StatusCode = StatusCodes.Status400BadRequest };

        var ext = Path.GetExtension(safeFileName).ToLowerInvariant();
        if (ext != ".m3u8" && ext != ".ts")
            return new HlsSegmentResult { StatusCode = StatusCodes.Status400BadRequest };

        var hlsDir = Path.GetFullPath(Path.Combine(General.TemporaryFilePath, "hls", fileKey));
        var filePath = Path.GetFullPath(Path.Combine(hlsDir, safeFileName));

        if (!filePath.StartsWith(hlsDir + Path.DirectorySeparatorChar) && filePath != hlsDir)
            return new HlsSegmentResult { StatusCode = StatusCodes.Status400BadRequest };

        if (!System.IO.File.Exists(filePath))
            return new HlsSegmentResult { StatusCode = StatusCodes.Status404NotFound };

        return new HlsSegmentResult
        {
            StatusCode = StatusCodes.Status200OK,
            Stream = System.IO.File.OpenRead(filePath),
            ContentType = ext == ".m3u8" ? "application/vnd.apple.mpegurl" : "video/MP2T",
            DisableCache = ext == ".m3u8"
        };
    }

    public void CleanupRoomUploadArtifacts(string uploadId)
    {
        if (string.IsNullOrWhiteSpace(uploadId))
            return;

        TryDeleteDirectory(GetUploadDirectory(uploadId));
        TryDeleteDirectory(Path.Combine(General.TemporaryFilePath, "hls", uploadId));
    }

    private async Task FinalizeRoomUploadAsync(string uploadId)
    {
        var uploadLock = GetUploadLock(uploadId);
        await uploadLock.WaitAsync();

        RoomUploadSessionState? session = null;

        try
        {
            session = await LoadSessionAsync(uploadId);
            if (session == null || session.State != RoomUploadStates.Processing)
                return;

            var uploadFilePath = GetUploadDataPath(uploadId);
            if (!System.IO.File.Exists(uploadFilePath))
                throw new FileNotFoundException("Upload session file could not be found", uploadFilePath);

            // For skipPlaylist (early-stream) uploads the file stays at the upload path so ffmpeg
            // can keep reading it. Once the upload is complete we can prepare HLS assets and
            // hand the room off to normal VOD playback without adding anything to the playlist.
            if (session.SkipPlaylist)
            {
                session.State = RoomUploadStates.Ready;
                session.FileKey = uploadId;
                session.PlaybackUrl = null;
                session.UpdatedUtc = DateTime.UtcNow;
                await SaveSessionAsync(session);

                var directPlaybackUrl = await TryBuildDirectRoomUploadPlaybackUrlAsync(uploadFilePath, uploadId, session);
                if (!string.IsNullOrWhiteSpace(directPlaybackUrl))
                {
                    session.PlaybackUrl = directPlaybackUrl;
                    session.UpdatedUtc = DateTime.UtcNow;
                    await SaveSessionAsync(session);

                    var room = MainManager.GetRoom(session.UniqueId);
                    if (room != null && room.IsRtmpFileShareActive && room.RtmpFileShareAssetKey == uploadId)
                    {
                        room.RtmpStreamUrl = session.PlaybackUrl;
                        room.RtmpFileShareUsesVodPlayback = true;
                        room.RtmpFileShareUploadId = null;
                        _rtmpFileShareManager.TransitionToVodPlayback(session.UniqueId);
                        await _hub.Clients.Group(session.UniqueId)
                            .rtmpFileShareVodReady(session.PlaybackUrl, room.RtmpFileShareDurationSec);
                    }

                    return;
                }

                if (IsVideoExtension(session.FileEnding) && !string.IsNullOrWhiteSpace(session.BaseUrl))
                {
                    var hlsDir = Path.Combine(General.TemporaryFilePath, "hls", uploadId);
                    Directory.CreateDirectory(hlsDir);

                    var hlsSucceeded = await GenerateHlsFromFileAsync(uploadFilePath, hlsDir, uploadId);
                    if (hlsSucceeded)
                    {
                        session.PlaybackUrl = $"{session.BaseUrl}/api/video/hlsSegment/{uploadId}/stream.m3u8";
                        session.UpdatedUtc = DateTime.UtcNow;
                        await SaveSessionAsync(session);

                        var room = MainManager.GetRoom(session.UniqueId);
                        if (room != null && room.IsRtmpFileShareActive && room.RtmpFileShareAssetKey == uploadId)
                        {
                            room.RtmpStreamUrl = session.PlaybackUrl;
                            room.RtmpFileShareUsesVodPlayback = true;
                            room.RtmpFileShareUploadId = null;
                            _rtmpFileShareManager.TransitionToVodPlayback(session.UniqueId);
                            await _hub.Clients.Group(session.UniqueId)
                                .rtmpFileShareVodReady(session.PlaybackUrl, room.RtmpFileShareDurationSec);
                        }
                    }
                    else
                    {
                        TryDeleteDirectory(hlsDir);
                    }
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(session.BaseUrl))
                throw new InvalidOperationException("Upload session is missing the playback base URL");

            if (RoomManager.GetRoom(session.UniqueId) == null)
            {
                session.State = RoomUploadStates.Failed;
                session.ErrorMessage = "Room not found";
                session.UpdatedUtc = DateTime.UtcNow;
                await SaveSessionAsync(session);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();

            var dbUser = await postgres.Users.FirstOrDefaultAsync(x => x.ID == session.UserId);
            if (dbUser == null)
                throw new InvalidOperationException("Upload owner could not be found");

            Directory.CreateDirectory(General.FilePath);

            var dbFile = new DbFile(session.FileName, session.FileEnding, dbUser);
            var finalFilePath = Path.Combine(General.FilePath, $"{dbFile.FileKey}{dbFile.FileEnding}");
            if (System.IO.File.Exists(finalFilePath)) System.IO.File.Delete(finalFilePath);

            System.IO.File.Move(uploadFilePath, finalFilePath, true);

            postgres.Files?.Add(dbFile);
            await postgres.SaveChangesAsync();
            await hub.Clients.Group(dbUser.ApiKey).updateFolders(new FileDto(dbFile));

            var playbackUrl = $"{session.BaseUrl}/api/video/fileByToken?fileKey={dbFile.FileKey}";

            // Server-side file-share uploads (SkipPlaylist) intentionally bypass the playlist:
            // the file is consumed by the SFU ffmpeg pipeline, not played by clients directly.
            if (!session.SkipPlaylist)
            {
                var addVideoResult = await RoomManager.AddVideo(
                    new DreckVideo(dbFile.Name, playbackUrl, false, TimeSpan.Zero, dbUser.username),
                    session.UniqueId);

                if (!string.IsNullOrWhiteSpace(addVideoResult))
                {
                    session.State = RoomUploadStates.Failed;
                    session.ErrorMessage = addVideoResult;
                    session.FileKey = dbFile.FileKey;
                    session.PlaybackUrl = playbackUrl;
                    session.UpdatedUtc = DateTime.UtcNow;
                    await SaveSessionAsync(session);
                    return;
                }
            }

            session.State = RoomUploadStates.Ready;
            session.ErrorMessage = null;
            session.FileKey = dbFile.FileKey;
            session.PlaybackUrl = playbackUrl;
            session.UpdatedUtc = DateTime.UtcNow;
            await SaveSessionAsync(session);

            // HLS pre-generation is also unnecessary for server-side ffmpeg shares;
            // the file is read directly by ffmpeg, not via HLS segments.
            if (!session.SkipPlaylist && IsVideoExtension(session.FileEnding))
                _ = Task.Run(() => TryGenerateHlsAssetsAsync(finalFilePath, dbFile.FileKey));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());

            if (session != null)
            {
                session.State = RoomUploadStates.Failed;
                session.ErrorMessage = "The upload finished, but playback preparation failed";
                session.UpdatedUtc = DateTime.UtcNow;
                await SaveSessionAsync(session);
            }
        }
        finally
        {
            uploadLock.Release();
        }
    }

    private async Task<DbUser?> GetUserByTokenAsync(string token)
    {
        return await _postgres.Users
            .Include(x => x.RememberTokens)
            .FirstOrDefaultAsync(x => x.RememberTokens.Any(y => y.Token == token));
    }

    private static readonly HashSet<string> FfmpegContainerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".mov", ".avi", ".flv", ".wmv", ".ts", ".m2ts", ".mts",
        ".hevc", ".webm", ".ogv", ".3gp", ".rm", ".rmvb", ".divx", ".asf",
        ".mp3", ".aac", ".flac", ".ogg", ".wav", ".m4a", ".opus", ".wma"
    };

    private static string? NormalizeExtensionForServerShare(string fileEnding)
    {
        if (string.IsNullOrWhiteSpace(fileEnding)) return null;
        if (!fileEnding.StartsWith('.')) fileEnding = '.' + fileEnding;
        fileEnding = Path.GetExtension("x" + fileEnding);
        if (string.IsNullOrWhiteSpace(fileEnding) || fileEnding == ".") return null;
        return FfmpegContainerExtensions.Contains(fileEnding) ? fileEnding : null;
    }

    private string? NormalizeAndValidateExtension(string fileEnding)
    {
        if (string.IsNullOrWhiteSpace(fileEnding)) return null;

        if (!fileEnding.StartsWith('.')) fileEnding = '.' + fileEnding;
        fileEnding = Path.GetExtension("x" + fileEnding);

        if (string.IsNullOrWhiteSpace(fileEnding) || fileEnding == ".") return null;

        if (!_contentTypeProvider.TryGetContentType("stream" + fileEnding, out var contentType)) return null;

        return contentType.StartsWith("video/") || contentType.StartsWith("audio/") ? fileEnding : null;
    }

    private bool IsVideoExtension(string fileEnding)
    {
        return _contentTypeProvider.TryGetContentType("stream" + fileEnding, out var contentType) &&
               contentType.StartsWith("video/");
    }

    private static SemaphoreSlim GetUploadLock(string uploadId)
    {
        return UploadLocks.GetOrAdd(uploadId, _ => new SemaphoreSlim(1, 1));
    }

    private static long GetUploadedBytes(string uploadId)
    {
        var dataPath = GetUploadDataPath(uploadId);
        return System.IO.File.Exists(dataPath) ? new FileInfo(dataPath).Length : 0;
    }

    private static string GetUploadRoot()
    {
        return Path.Combine(General.TemporaryFilePath, "room-uploads");
    }

    private static string GetUploadDirectory(string uploadId)
    {
        return Path.Combine(GetUploadRoot(), uploadId);
    }

    private static string GetUploadDataPath(string uploadId)
    {
        return Path.Combine(GetUploadDirectory(uploadId), "upload.bin");
    }

    private static string GetUploadMetadataPath(string uploadId)
    {
        return Path.Combine(GetUploadDirectory(uploadId), "session.json");
    }

    private static async Task<RoomUploadSessionState?> LoadSessionAsync(string uploadId)
    {
        var metadataPath = GetUploadMetadataPath(uploadId);
        if (!System.IO.File.Exists(metadataPath)) return null;

        var json = await System.IO.File.ReadAllTextAsync(metadataPath);
        return JsonSerializer.Deserialize<RoomUploadSessionState>(json, JsonOptions);
    }

    private static async Task SaveSessionAsync(RoomUploadSessionState session)
    {
        Directory.CreateDirectory(GetUploadDirectory(session.UploadId));
        var json = JsonSerializer.Serialize(session, JsonOptions);
        await System.IO.File.WriteAllTextAsync(GetUploadMetadataPath(session.UploadId), json);
    }

    private static RoomUploadSessionResult ToRoomUploadResult(
        RoomUploadSessionState session,
        long nextOffset,
        int statusCode)
    {
        return new RoomUploadSessionResult
        {
            StatusCode = statusCode,
            ErrorMessage = session.ErrorMessage,
            UploadId = session.UploadId,
            State = session.State,
            ChunkSize = DefaultRoomUploadChunkSize,
            NextOffset = nextOffset,
            TotalSize = session.TotalSize,
            FileKey = session.FileKey,
            PlaybackUrl = session.PlaybackUrl
        };
    }

    private static async Task<bool> GenerateHlsFromFileAsync(string inputPath, string hlsDir, string fileKey)
    {
        var segmentPattern = Path.Combine(hlsDir, "seg%05d.ts").Replace('\\', '/');
        var m3u8Path = Path.Combine(hlsDir, "stream.m3u8").Replace('\\', '/');

        var args = $"-y -i \"{inputPath}\" " +
                   "-map 0:v:0 -map 0:a? " +
                   "-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" " +
                   "-c:v libx264 -preset veryfast -crf 23 " +
                   "-c:a aac -b:a 128k -ac 2 " +
                   "-f hls -hls_time 4 -hls_list_size 0 " +
                   $"-hls_segment_filename \"{segmentPattern}\" " +
                   $"\"{m3u8Path}\"";

        await RunFFmpegProcessAsync(args, fileKey, "finalize-hls");
        return HasUsableHlsOutput(hlsDir);
    }

    private static async Task TryGenerateHlsAssetsAsync(string inputPath, string fileKey)
    {
        var hlsDir = Path.Combine(General.TemporaryFilePath, "hls", fileKey);

        try
        {
            Directory.CreateDirectory(hlsDir);

            var hlsSucceeded = await GenerateHlsFromFileAsync(inputPath, hlsDir, fileKey);
            if (!hlsSucceeded)
                TryDeleteDirectory(hlsDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            TryDeleteDirectory(hlsDir);
        }
    }

    private static async Task RunFFmpegProcessAsync(string args, string fileKey, string step)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = General.GetFFmpegPath(),
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = true
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine($"[HLS {step} {fileKey}] {e.Data}");
        };

        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        Console.WriteLine($"[HLS {step} {fileKey}] exit={process.ExitCode}");
    }

    private async Task<string?> TryBuildDirectRoomUploadPlaybackUrlAsync(
        string uploadFilePath,
        string uploadId,
        RoomUploadSessionState session)
    {
        if (string.IsNullOrWhiteSpace(session.BaseUrl))
            return null;

        var canUseDirectPlayback = await CanUseDirectPlaybackAsync(uploadFilePath, session.FileEnding);
        if (!canUseDirectPlayback)
            return null;

        return $"{session.BaseUrl}/api/video/roomUploadByToken?uploadId={Uri.EscapeDataString(uploadId)}";
    }

    private static async Task<bool> CanUseDirectPlaybackAsync(string filePath, string fileEnding)
    {
        var streams = await ProbeStreamCodecsAsync(filePath);
        if (streams.Count == 0)
            return false;

        var normalizedExtension = (fileEnding ?? string.Empty).Trim().ToLowerInvariant();
        var videoCodecs = streams
            .Where(stream => string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase))
            .Select(stream => stream.CodecName)
            .Where(codec => !string.IsNullOrWhiteSpace(codec))
            .ToList();
        var audioCodecs = streams
            .Where(stream => string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase))
            .Select(stream => stream.CodecName)
            .Where(codec => !string.IsNullOrWhiteSpace(codec))
            .ToList();

        var isSupported = normalizedExtension switch
        {
            ".mp4" or ".m4v" or ".mov" =>
                AllCodecsSupported(videoCodecs, DirectMp4VideoCodecs) &&
                AllCodecsSupported(audioCodecs, DirectMp4AudioCodecs),
            ".webm" =>
                AllCodecsSupported(videoCodecs, DirectWebmVideoCodecs) &&
                AllCodecsSupported(audioCodecs, DirectWebmAudioCodecs),
            ".mp3" =>
                videoCodecs.Count == 0 && AllCodecsSupported(audioCodecs, DirectMp3AudioCodecs),
            ".m4a" or ".aac" =>
                videoCodecs.Count == 0 && AllCodecsSupported(audioCodecs, DirectAacAudioCodecs),
            ".ogg" or ".oga" =>
                videoCodecs.Count == 0 && AllCodecsSupported(audioCodecs, DirectOggAudioCodecs),
            ".wav" =>
                videoCodecs.Count == 0 && AllCodecsSupported(audioCodecs, DirectWavAudioCodecs),
            _ => false,
        };

        Console.WriteLine(
            $"[RoomUpload/Direct] directPlaybackCandidate extension={normalizedExtension} " +
            $"video=[{string.Join(",", videoCodecs)}] audio=[{string.Join(",", audioCodecs)}] supported={isSupported}");

        return isSupported;
    }

    private static bool AllCodecsSupported(IReadOnlyCollection<string> codecs, HashSet<string> supportedCodecs)
        => codecs.Count == 0 || codecs.All(codec => supportedCodecs.Contains(codec));

    private static async Task<List<PlaybackProbeStream>> ProbeStreamCodecsAsync(string filePath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = General.GetFFprobePath(),
                    Arguments = $"-v error -print_format json -show_entries stream=codec_type,codec_name \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                Console.WriteLine(
                    $"[RoomUpload/Probe] ffprobe failed exitCode={process.ExitCode} stdoutEmpty={string.IsNullOrWhiteSpace(stdout)} stderr=\"{TrimForLog(stderr, 300)}\"");
                return new List<PlaybackProbeStream>();
            }

            var streams = JsonNode.Parse(stdout)?["streams"]?.AsArray();
            if (streams == null)
                return new List<PlaybackProbeStream>();

            return streams
                .Select(stream => new PlaybackProbeStream
                {
                    CodecType = stream?["codec_type"]?.GetValue<string>() ?? string.Empty,
                    CodecName = stream?["codec_name"]?.GetValue<string>() ?? string.Empty,
                })
                .Where(stream => !string.IsNullOrWhiteSpace(stream.CodecType) && !string.IsNullOrWhiteSpace(stream.CodecName))
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RoomUpload/Probe] ffprobe playback probe failed: {ex.Message}");
            return new List<PlaybackProbeStream>();
        }
    }

    private static string TrimForLog(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static bool HasUsableHlsOutput(string hlsDir)
    {
        try
        {
            var manifestPath = Path.Combine(hlsDir, "stream.m3u8");
            if (!System.IO.File.Exists(manifestPath)) return false;

            var manifestText = System.IO.File.ReadAllText(manifestPath);
            if (string.IsNullOrWhiteSpace(manifestText) || !manifestText.Contains("#EXTM3U")) return false;

            var segmentFiles = Directory.GetFiles(hlsDir, "*.ts");
            return segmentFiles.Any(file => new FileInfo(file).Length > 0);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup errors
        }
    }

    private sealed class RoomUploadSessionState
    {
        public string UploadId { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string UserApiKey { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string UniqueId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileEnding { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public string State { get; set; } = RoomUploadStates.Uploading;
        public string? ErrorMessage { get; set; }
        public string? FileKey { get; set; }
        public string? PlaybackUrl { get; set; }
        public string? BaseUrl { get; set; }
        /// <summary>
        /// When true, the file is uploaded for purposes other than playlist playback
        /// (e.g. server-side WebRTC file share) and must NOT be added to the room's video playlist.
        /// </summary>
        public bool SkipPlaylist { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class PlaybackProbeStream
    {
        public string CodecType { get; init; } = string.Empty;
        public string CodecName { get; init; } = string.Empty;
    }

    private static class RoomUploadStates
    {
        public const string Uploading = "uploading";
        public const string Processing = "processing";
        public const string Ready = "ready";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";
    }
}