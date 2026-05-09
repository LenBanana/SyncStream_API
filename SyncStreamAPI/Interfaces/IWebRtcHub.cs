using System.Threading.Tasks;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task startWebRtcStream(string connectionId, string roomId);
    /// <summary>Sent to all room members when a moderator forces a broadcast — viewers should auto-join.</summary>
    Task broadcastStream(string streamerId, string roomId);
    Task sendOfferToViewer(WebRtcClientOffer offer);
    Task getWebRtcCredentials(WebRtcCredentials credentials);
    Task stopWebRtcStream(string connectionId);
    Task joinWebRtcStream(string connectionId);
    Task sendClientAnswer(WebRtcClientOffer answer);
    Task sendIceCandidate(object iceCandidate);
    /// <summary>Sent to the streamer when a viewer sends an ICE candidate, with the viewer's connectionId included.</summary>
    Task sendIceCandidateFromViewer(string viewerId, object iceCandidate);

    /// <summary>
    /// Pushed to viewers when the room's active stream is via the SFU.
    /// The viewer should call JoinSfuRoom(roomId) in response.
    /// </summary>
    Task startSfuStream(string roomId, string streamerId);

    /// <summary>
    /// Sent to all room members when a user begins sharing a local file via WebRTC/SFU.
    /// Viewers should call JoinSfuRoom(roomId) in response.
    /// </summary>
    Task fileShareStarted(string streamerId, string username, string roomId);

    /// <summary>Sent to all room members when the active file share ends.</summary>
    Task fileShareStopped();

    /// <summary>
    /// Sent by the server to all non-host room members to sync play/pause state
    /// while a file share is active.
    /// </summary>
    Task fileSharePlayPause(bool isPlaying);

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