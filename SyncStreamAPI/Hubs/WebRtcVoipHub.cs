using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models.WebRTC;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task JoinAudioRoom(string token, string roomId)
        {
            var user = await MainManager.GetUser(token);
            if (user == null) return;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"AudioRoom-{roomId}");
            await Clients.GroupExcept($"AudioRoom-{roomId}", new[] { Context.ConnectionId })
                .participantJoined(new VoipParticipantDto()
                    { ParticipantId = Context.ConnectionId, ParticipantName = user.username });
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task LeaveAudioRoom(string token, string roomId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"AudioRoom-{roomId}");
            await Clients.GroupExcept($"AudioRoom-{roomId}", new[] { Context.ConnectionId })
                .participantLeft(new VoipParticipantDto() { ParticipantId = Context.ConnectionId });
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task SendStatusToParticipant(string token, VoipParticipantDto participantDto, string roomId)
        {
            participantDto.ParticipantId = Context.ConnectionId;
            await Clients.GroupExcept($"AudioRoom-{roomId}", new[] { Context.ConnectionId })
                .receiveStatusFromParticipant(participantDto);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task SendOfferToParticipant(string token, string participantId, VoipOffer offer)
        {
            var user = await MainManager.GetUser(token);
            if (user == null) return;
            offer.ParticipantName = user.username;
            await Clients.Client(participantId).receiveOfferFromParticipant(Context.ConnectionId, offer);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task SendAnswerToParticipant(string token, string participantId, VoipOffer answer)
        {
            var user = await MainManager.GetUser(token);
            if (user == null) return;
            answer.ParticipantName = user.username;
            await Clients.Client(participantId).receiveAnswerFromParticipant(Context.ConnectionId, answer);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task SendIceCandidateToParticipant(string token, string participantId, VoipIceCandidate candidate)
        {
            await Clients.Client(participantId).receiveIceCandidateFromParticipant(Context.ConnectionId, candidate);
        }
    }
}