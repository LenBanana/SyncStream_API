using System;
using System.Diagnostics;

namespace SyncStreamAPI.Models.RTMP;

/// <summary>
/// In-memory record of one active RTMP file-share session.
/// Lifecycle: created by RtmpFileShareManager.StartAsync, destroyed by StopAsync or disconnect cleanup.
/// </summary>
public class RtmpFileShareSession
{
    public string RoomId { get; set; } = string.Empty;
    public string OwnerConnectionId { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    /// <summary>Lowercase username — used as the stream name in the RTMP URL.</summary>
    public string OwnerUsername { get; set; } = string.Empty;
    /// <summary>Absolute path to the upload.bin file being streamed.</summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>Non-null while the chunk upload is still in progress; null once finalised. Blocks seeking.</summary>
    public string? UploadId { get; set; }
    /// <summary>The running ffmpeg process; null while paused.</summary>
    public Process? Process { get; set; }
    /// <summary>
    /// Saved playback position (seconds) at the last pause or seek.
    /// When ffmpeg is running, add wall-clock elapsed to this to get the live position.
    /// </summary>
    public double PositionSec { get; set; }
    /// <summary>UTC time when the current ffmpeg process was started (wall-clock anchor).</summary>
    public DateTime StartedAtUtc { get; set; }
    public bool Paused { get; set; }
    /// <summary>Total duration in seconds probed via ffprobe; null if unavailable.</summary>
    public double? DurationSec { get; set; }
    /// <summary>The stream token used as RTMP auth query param.</summary>
    public string StreamToken { get; set; } = string.Empty;

    /// <summary>
    /// Computes the current playback position based on the saved offset and
    /// how long the current ffmpeg process has been running — mirrors the
    /// currentPositionSec getter in sfu/server-file-streamer.js.
    /// </summary>
    public double CurrentPositionSec
    {
        get
        {
            if (Paused || Process == null)
                return PositionSec;
            return PositionSec + (DateTime.UtcNow - StartedAtUtc).TotalSeconds;
        }
    }
}
