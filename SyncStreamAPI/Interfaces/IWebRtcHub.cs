using SyncStreamAPI.Models.WebRTC;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task sendClientOffer(WebRtcClientOffer offer);
        Task sendClientAnswer(WebRtcClientOffer answer);
        Task sendIceCandidate(WebRtcIceCandidate iceCandidate);
    }
}
