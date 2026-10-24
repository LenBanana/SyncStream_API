using System.Threading.Tasks;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task participantJoined(VoipParticipantDto participant);
    Task participantLeft(VoipParticipantDto participant);
    Task receiveStatusFromParticipant(VoipParticipantDto participant);
}
