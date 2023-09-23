using SyncStreamAPI.Models.WebRTC;
using System.Threading.Tasks;
using Org.WebRtc;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task startWebRtcStream(string connectionId);
        Task sendOfferToViewer(WebRtcClientOffer offer);
        Task getWebRtcCredentials(WebRtcCredentials credentials);
        Task stopWebRtcStream(string connectionId);
        Task joinWebRtcStream(string connectionId);
        Task sendClientAnswer(WebRtcClientOffer answer);
        Task sendIceCandidate(object iceCandidate);
    }
}
