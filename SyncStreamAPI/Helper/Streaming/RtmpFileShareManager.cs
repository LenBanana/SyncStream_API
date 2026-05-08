using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models.RTMP;

namespace SyncStreamAPI.Helper.Streaming;

/// <summary>
/// Manages the lifecycle of RTMP file-share sessions: spawning/killing ffmpeg processes,
/// tracking playback position, and broadcasting position ticks to rooms via SignalR.
/// Registered as a singleton in DI.
/// </summary>
public class RtmpFileShareManager : IDisposable
{
    private readonly string _rtmpBaseUrl;
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly ConcurrentDictionary<string, RtmpFileShareSession> _sessions = new();
    private readonly Timer _positionTimer;

    public RtmpFileShareManager(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub)
    {
        _hub = hub;
        // e.g. "rtmp://rtmp-server:1935/live"
        _rtmpBaseUrl = configuration["RtmpUrl"]?.TrimEnd('/') ?? "rtmp://rtmp-server:1935/live";
        // Broadcast position to all rooms every 5 seconds.
        _positionTimer = new Timer(OnPositionTick, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public RtmpFileShareSession? Get(string roomId)
        => _sessions.TryGetValue(roomId, out var s) ? s : null;

    /// <summary>
    /// Creates a session, probes file duration, and spawns ffmpeg.
    /// </summary>
    public async Task<RtmpFileShareSession> StartAsync(
        string roomId, string ownerConnectionId, int userId, string username,
        string streamToken, string filePath, string? uploadId)
    {
        var session = new RtmpFileShareSession
        {
            RoomId            = roomId,
            OwnerConnectionId = ownerConnectionId,
            OwnerUserId       = userId,
            OwnerUsername     = username.ToLower(),
            StreamToken       = streamToken,
            FilePath          = filePath,
            UploadId          = uploadId,
            PositionSec       = 0,
            Paused            = false,
        };

        _sessions[roomId] = session;

        // Probe duration in the background — does not block stream start.
        _ = Task.Run(async () =>
        {
            try { session.DurationSec = await ProbeFileDurationAsync(filePath); }
            catch { /* probe failure is non-fatal */ }
        });

        SpawnFfmpeg(session, 0);
        return session;
    }

    /// <summary>Toggles play/pause for the session.</summary>
    public Task PausePlayAsync(string roomId, bool isPlaying)
    {
        if (!_sessions.TryGetValue(roomId, out var session)) return Task.CompletedTask;

        if (isPlaying && session.Paused)
        {
            session.Paused = false;
            SpawnFfmpeg(session, session.PositionSec);
        }
        else if (!isPlaying && !session.Paused)
        {
            session.Paused = true;
            KillFfmpeg(session);
        }
        return Task.CompletedTask;
    }

    /// <summary>Kills the current ffmpeg process and restarts from the new position.</summary>
    public Task SeekAsync(string roomId, double positionSec)
    {
        if (!_sessions.TryGetValue(roomId, out var session)) return Task.CompletedTask;
        KillFfmpeg(session);
        session.PositionSec = positionSec;
        session.Paused = false;
        SpawnFfmpeg(session, positionSec);
        return Task.CompletedTask;
    }

    /// <summary>Stops the session, kills ffmpeg, cleans up the upload directory.</summary>
    /// <returns>True if a session was actually stopped.</returns>
    public bool Stop(string roomId)
    {
        if (!_sessions.TryRemove(roomId, out var session)) return false;
        KillFfmpeg(session);
        CleanUpUploadDir(session);
        return true;
    }

    /// <summary>Clears the upload-guard so seeking becomes available.</summary>
    public void MarkUploadComplete(string roomId, string uploadId)
    {
        if (_sessions.TryGetValue(roomId, out var session) && session.UploadId == uploadId)
            session.UploadId = null;
    }

    // ── ffmpeg process management ─────────────────────────────────────────────

    private void SpawnFfmpeg(RtmpFileShareSession session, double fromPositionSec)
    {
        var seekArg = fromPositionSec > 0.5
            ? $"-ss {fromPositionSec.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} "
            : string.Empty;

        var rtmpUrl = $"{_rtmpBaseUrl}/{session.OwnerUsername}?token={Uri.EscapeDataString(session.StreamToken)}";

        var args = string.Concat(
            $"-re {seekArg}",
            $"-i \"{session.FilePath}\" ",
            "-c:v libx264 -preset veryfast -tune zerolatency ",
            "-profile:v main -level:v 4.1 -pix_fmt yuv420p ",
            "-b:v 5000k -maxrate 6000k -bufsize 10000k ",
            "-g 60 -keyint_min 60 -sc_threshold 0 ",
            "-c:a aac -b:a 192k -ar 48000 -ac 2 ",
            $"-f flv \"{rtmpUrl}\""
        );

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = General.GetFFmpegPath(),
                Arguments              = args,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            },
            EnableRaisingEvents = true
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"[RtmpShare/{session.RoomId.Substring(0, Math.Min(8, session.RoomId.Length))}] {e.Data}");
        };

        process.Exited += (_, _) =>
        {
            // If ffmpeg exits unexpectedly while the session is still active and not paused,
            // log it — the user may need to restart via Stop+Start.
            if (_sessions.ContainsKey(session.RoomId) && !session.Paused)
                Console.WriteLine($"[RtmpShare] ffmpeg exited unexpectedly for room {session.RoomId}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        session.Process      = process;
        session.StartedAtUtc = DateTime.UtcNow;
    }

    private static void KillFfmpeg(RtmpFileShareSession session)
    {
        var p = session.Process;
        if (p == null) return;
        // Save position before killing so seek/pause resume from the right offset.
        session.PositionSec = session.CurrentPositionSec;
        session.Process = null;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(2000);
            }
        }
        catch { /* best-effort */ }
        finally { p.Dispose(); }
    }

    private static void CleanUpUploadDir(RtmpFileShareSession session)
    {
        // Derive the upload directory from FilePath so cleanup works even after UploadId is cleared.
        var dir = Path.GetDirectoryName(session.FilePath);
        if (string.IsNullOrWhiteSpace(dir)) return;
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Console.WriteLine($"[RtmpShare] Failed to delete upload dir: {ex.Message}"); }
    }

    // ── ffprobe duration probe ────────────────────────────────────────────────

    private static async Task<double?> ProbeFileDurationAsync(string filePath)
    {
        var tcs = new TaskCompletionSource<double?>();
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = General.GetFFprobePath(),
                Arguments              = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            }
        };

        p.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data) &&
                double.TryParse(e.Data.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var d))
            {
                tcs.TrySetResult(d);
            }
        };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        cts.Token.Register(() => tcs.TrySetResult(null));

        await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        tcs.TrySetResult(null);
        return await tcs.Task;
    }

    // ── Position broadcast timer ──────────────────────────────────────────────

    private void OnPositionTick(object? _)
    {
        foreach (var (roomId, session) in _sessions)
        {
            if (session.Paused) continue;
            var pos = session.CurrentPositionSec;
            _ = _hub.Clients.Group(roomId).rtmpFileSharePosition(pos);
        }
    }

    public void Dispose()
    {
        _positionTimer.Dispose();
        foreach (var session in _sessions.Values)
            KillFfmpeg(session);
        _sessions.Clear();
    }
}
