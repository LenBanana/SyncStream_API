using System.Threading.Tasks;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task receiveOfferFromParticipant(string senderId, VoipOffer offer);
    Task receiveAnswerFromParticipant(string senderId, VoipOffer answer);
    Task receiveIceCandidateFromParticipant(string senderId, object candidate);
    Task participantJoined(VoipParticipantDto participantId);
    Task participantLeft(VoipParticipantDto participantId);
    Task receiveStatusFromParticipant(VoipParticipantDto participantId);
}