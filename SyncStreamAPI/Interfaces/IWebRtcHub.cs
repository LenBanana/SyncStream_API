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
}