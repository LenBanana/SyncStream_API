using System.Threading.Tasks;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task startSFUStream(string connectionId);
    Task stopSFUStream(string connectionId);
    Task joinSFUStream(string connectionId);
    Task sendAnswerToSFU(WebRtcClientOffer answer);
    Task sendIceCandidateToSFU(WebRtcIceCandidate iceCandidate);
}