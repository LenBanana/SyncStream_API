using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    /// <summary>
    /// Sent to the room when a broadcast or a member-list live stream ends. Viewers watching that streamer
    /// leave its SFU room.
    /// </summary>
    Task stopWebRtcStream(string connectionId);

    /// <summary>
    /// Sent to the room when a member broadcasts to the room's stage. Viewers join the stage's SFU room.
    /// </summary>
    Task startSfuStream(string roomId, string streamerId);

    /// <summary>
    /// Sent to all room members when a user begins sharing a file to the stage.
    /// Viewers join the stage's SFU room in response.
    /// </summary>
    Task fileShareStarted(string streamerId, string username, string roomId);

    /// <summary>Sent to all room members when the active file share ends.</summary>
    Task fileShareStopped();

    /// <summary>Sent to the other room members when the sharer pauses or resumes the file.</summary>
    Task fileSharePlayPause(bool isPlaying);

    /// <summary>
    /// Periodic state of a file the server is streaming: where it is, and whether it is waiting for more of
    /// the upload.
    /// </summary>
    Task serverFileShareState(double positionSec, bool buffering);

    // ── RTMP file-share events ─────────────────────────────────────────────────

    /// <summary>Sent to all room members when the host starts an RTMP file-share.</summary>
    Task rtmpFileShareStarted(string streamerId, string username, string roomId,
                              string streamUrl, double? durationSec);
    /// <summary>Sent to all room members when the RTMP file-share ends.</summary>
    Task rtmpFileShareStopped();
    /// <summary>Sent to viewers when the host pauses or resumes the RTMP stream.</summary>
    Task rtmpFileSharePlayPause(bool isPlaying, double positionSec);
    /// <summary>Sent to viewers when the host seeks; clients should reconnect their player.</summary>
    Task rtmpFileShareSeek(double positionSec);
    /// <summary>Periodic position broadcast so viewers can sync their seek bar.</summary>
    Task rtmpFileSharePosition(double positionSec);
    /// <summary>Sent when RTMP file-share metadata changes, e.g. duration probe or seek availability.</summary>
    Task rtmpFileShareMetadata(double? durationSec, bool canSeek);
    /// <summary>Sent when a restarted RTMP publisher is live again and clients may reconnect.</summary>
    Task rtmpFileShareStreamReady(double positionSec);
    /// <summary>Sent when the finished upload is ready for normal VOD playback.</summary>
    Task rtmpFileShareVodReady(string playbackUrl, double? durationSec, string? contentType);
}
