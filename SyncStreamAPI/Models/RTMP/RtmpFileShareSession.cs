using System;
using System.Diagnostics;
using System.Threading;

namespace SyncStreamAPI.Models.RTMP;

public enum RtmpFileShareState
{
    /// <summary>ffmpeg is streaming, or about to be started again after a failed run.</summary>
    Playing,

    /// <summary>The sharer paused; nothing is running.</summary>
    Paused,

    /// <summary>Playback caught up with the upload; waiting for more of the file to arrive.</summary>
    Buffering,

    /// <summary>The file has played out.</summary>
    Ended,

    /// <summary>ffmpeg kept failing, or the upload stopped arriving.</summary>
    Failed,
}

/// <summary>Where a share stands, read in one go so the parts agree with each other.</summary>
public sealed record RtmpFileShareStatus(
    RtmpFileShareState State,
    double PositionSec,
    double? DurationSec,
    bool UploadComplete,
    bool PublisherLive,
    string? Error)
{
    public bool IsOver => State is RtmpFileShareState.Ended or RtmpFileShareState.Failed;
}

/// <summary>
///     One file being streamed to a room through nginx-rtmp. Created by RtmpFileShareManager.PrepareAsync and
///     removed by its Stop.
/// </summary>
/// <remarks>Everything that changes after creation is read and written under <see cref="Gate" />.</remarks>
public class RtmpFileShareSession
{
    public object Gate { get; } = new();

    public required string RoomId { get; init; }

    /// <summary>
    ///     The share's own stream name on the streaming server. It is not the sharer's OBS stream: a share used
    ///     to publish under the sharer's username, which listed a room's file as the sharer going live for every
    ///     signed-in user, and needed the sharer's stream token, which not every account has.
    /// </summary>
    public required string StreamName { get; init; }

    /// <summary>What ffmpeg presents as its token when it publishes the stream.</summary>
    public required string PublishKey { get; init; }

    /// <summary>The upload.bin being streamed, which may still be growing.</summary>
    public required string FilePath { get; init; }

    public required string UploadId { get; init; }
    public required long TotalBytes { get; init; }
    public bool UploadComplete { get; set; }
    public double? DurationSec { get; init; }

    /// <summary>ffmpeg map specifier for the selected audio track.</summary>
    public string AudioMapSpecifier { get; set; } = "0:a:0?";
    /// <summary>Human-readable description of the selected audio track for logs.</summary>
    public string AudioSelectionLabel { get; set; } = "first audio fallback";
    /// <summary>Optional ffmpeg video filter fragment that burns a subtitle track into the video.</summary>
    public string? SubtitleFilter { get; set; }
    /// <summary>Kind-relative subtitle stream ordinal selected for burning, e.g. 0 for 0:s:0.</summary>
    public int? SubtitleOrdinal { get; set; }
    /// <summary>Burn strategy for the selected subtitle stream: text uses libass, bitmap uses overlay.</summary>
    public string? SubtitleBurnMethod { get; set; }
    /// <summary>Human-readable description of the selected subtitle track for logs.</summary>
    public string? SubtitleSelectionLabel { get; set; }
    /// <summary>Normalized playback preferences requested by the host for audio/subtitle selection.</summary>
    public RtmpPlaybackPreferences PlaybackPreferences { get; init; } = new();
    /// <summary>True once ffprobe successfully read stream metadata and selection no longer relies on a fallback.</summary>
    public bool PlaybackSelectionResolved { get; set; }

    public RtmpFileShareState State { get; set; }
    public string? Error { get; set; }

    /// <summary>The running ffmpeg; null while nothing runs.</summary>
    public Process? Process { get; set; }

    /// <summary>True while the streaming server has the current run's stream.</summary>
    public bool IsPublisherLive { get; set; }

    /// <summary>Where the current run started, or where the next one starts while nothing runs.</summary>
    public double PositionSec { get; set; }

    /// <summary>How far the current run has got, from ffmpeg's own progress report.</summary>
    public double RunProgressSec { get; set; }

    /// <summary>When <see cref="RunProgressSec" /> was last reported; null until the run has sent anything.</summary>
    public DateTime? RunProgressAtUtc { get; set; }

    /// <summary>Failed runs in a row, counted once the upload is complete.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>The last thing ffmpeg complained about, to tell the sharer when the share has to stop.</summary>
    public string? LastFfmpegError { get; set; }

    /// <summary>Cancels whatever the share is waiting on: more of the upload, or a retry.</summary>
    public CancellationTokenSource? Waiting { get; set; }

    /// <summary>
    ///     The playback position. A run's progress arrives once a second, so the time since the last report is
    ///     added, up to a little more than that second, and nothing is added before a run has reported at all:
    ///     ffmpeg spends that time opening the file and connecting, not streaming.
    /// </summary>
    public double CurrentPositionSec
    {
        get
        {
            var position = PositionSec;
            if (Process != null && RunProgressAtUtc is { } reportedAt)
                position += RunProgressSec + Math.Clamp((DateTime.UtcNow - reportedAt).TotalSeconds, 0, 1.5);
            return DurationSec is > 0 ? Math.Min(position, DurationSec.Value) : position;
        }
    }

    public RtmpFileShareStatus Status()
    {
        lock (Gate)
            return new RtmpFileShareStatus(State, CurrentPositionSec, DurationSec, UploadComplete, IsPublisherLive, Error);
    }
}
