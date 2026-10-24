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

    /// <summary>
    /// Sent to all room members when a member starts an RTMP file share. Players ask GetLivePlayUrl for the
    /// stream's URL once it is live.
    /// </summary>
    Task rtmpFileShareStarted(string streamerId, string username, string roomId,
                              string streamName, double? durationSec);
    /// <summary>Sent to all room members when the RTMP file-share ends.</summary>
    Task rtmpFileShareStopped();
    /// <summary>Sent to the room when the sharer pauses or resumes.</summary>
    Task rtmpFileSharePlayPause(bool isPlaying, double positionSec);
    /// <summary>Sent to the room when the sharer seeks; players reconnect once the stream is live again.</summary>
    Task rtmpFileShareSeek(double positionSec);
    /// <summary>
    /// Periodic state of the share: where it is, and whether it is waiting for more of the upload.
    /// </summary>
    Task rtmpFileShareState(double positionSec, bool buffering);
    /// <summary>Sent when the share's duration is known or it becomes seekable.</summary>
    Task rtmpFileShareMetadata(double? durationSec, bool canSeek);
    /// <summary>
    /// Sent when a run of the share starts publishing. Every run is a new stream with its own timestamps, so
    /// players connect afresh.
    /// </summary>
    Task rtmpFileShareStreamReady(double positionSec);
}
