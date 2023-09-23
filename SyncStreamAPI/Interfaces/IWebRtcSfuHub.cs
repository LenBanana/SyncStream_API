using System.Threading.Tasks;
using Org.WebRtc;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task receiveOfferFromServer(string senderId, RTCSessionDescription offer);
    Task receiveAnswerFromServer(string senderId, RTCSessionDescription answer);
    Task receiveIceCandidateFromServer(string senderId, RTCIceCandidate candidate);
    Task receiveStatusFromServer(VoipParticipantDto participantId);
    Task sfuParticipantJoined(VoipParticipantDto participantId);
    Task sfuParticipantLeft(VoipParticipantDto participantId);
}