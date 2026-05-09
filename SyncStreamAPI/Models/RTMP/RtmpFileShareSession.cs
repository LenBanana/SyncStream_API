using System;
using System.Diagnostics;
using System.Collections.Generic;

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
    /// <summary>ffmpeg map specifier for the selected audio track.</summary>
    public string AudioMapSpecifier { get; set; } = "0:a:0?";
    /// <summary>Human-readable description of the selected audio track for logs.</summary>
    public string AudioSelectionLabel { get; set; } = "first audio fallback";
    /// <summary>Optional ffmpeg video filter fragment that burns a subtitle track into the video.</summary>
    public string? SubtitleFilter { get; set; }
    /// <summary>Human-readable description of the selected subtitle track for logs.</summary>
    public string? SubtitleSelectionLabel { get; set; }
    /// <summary>Normalized playback preferences requested by the host for audio/subtitle selection.</summary>
    public RtmpPlaybackPreferences PlaybackPreferences { get; set; } = new();
    /// <summary>True once ffprobe successfully read stream metadata and selection no longer relies on a fallback.</summary>
    public bool PlaybackSelectionResolved { get; set; }
    /// <summary>Number of consecutive unexpected ffmpeg exits since the last explicit start/seek/resume.</summary>
    public int RetryCount { get; set; }
    /// <summary>
    /// Counts intentional ffmpeg shutdowns (pause/seek) so nginx-rtmp callbacks can
    /// distinguish a managed restart from a real stream end.
    /// </summary>
    public int PendingPublisherDisconnects { get; set; }
    /// <summary>Skip upload-dir cleanup once the share has handed off to VOD playback.</summary>
    public bool PreserveUploadArtifacts { get; set; }
    /// <summary>Latest ffmpeg out_time value in seconds for the current process lifetime.</summary>
    public double LastEncoderOutTimeSec { get; set; }
    /// <summary>UTC time when <see cref="LastEncoderOutTimeSec"/> was last refreshed.</summary>
    public DateTime? LastEncoderProgressAtUtc { get; set; }
    /// <summary>True once nginx-rtmp has accepted the current publisher instance.</summary>
    public bool IsPublisherLive { get; set; }
    /// <summary>
    /// True for MP4/MOV files: ffmpeg cannot open them until the moov atom is present,
    /// so we defer spawning until a qt-faststart remux completes after upload finishes.
    /// </summary>
    public bool NeedsRemux { get; set; }
    /// <summary>Viewer HLS URL — stored so the manager can fire rtmpFileShareStarted after the deferred remux.</summary>
    public string StreamUrl { get; set; } = string.Empty;

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

            if (LastEncoderProgressAtUtc.HasValue)
            {
                var encoderWallClockAdvance = Math.Max(0, (DateTime.UtcNow - LastEncoderProgressAtUtc.Value).TotalSeconds);
                return PositionSec + LastEncoderOutTimeSec + Math.Min(encoderWallClockAdvance, 1.5d);
            }

            return PositionSec + (DateTime.UtcNow - StartedAtUtc).TotalSeconds;
        }
    }
}
