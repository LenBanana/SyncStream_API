using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models.WebRTC;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task JoinAudioRoom(string token, string roomId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"AudioRoom-{roomId}");
            // Notify existing participants about the new user
            await Clients.GroupExcept($"AudioRoom-{roomId}", new[] { Context.ConnectionId })
                .participantJoined(Context.ConnectionId);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task LeaveAudioRoom(string token, string roomId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"AudioRoom-{roomId}");
            // Notify existing participants about the new user
            await Clients.GroupExcept($"AudioRoom-{roomId}", new[] { Context.ConnectionId })
                .participantLeft(Context.ConnectionId);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task SendOfferToParticipant(string token, string participantId, VoipOffer offer)
        {
            await Clients.Client(participantId).receiveOfferFromParticipant(Context.ConnectionId, offer);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task SendAnswerToParticipant(string token, string participantId, VoipOffer answer)
        {
            await Clients.Client(participantId).receiveAnswerFromParticipant(Context.ConnectionId, answer);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task SendIceCandidateToParticipant(string token, string participantId, VoipIceCandidate candidate)
        {
            await Clients.Client(participantId).receiveIceCandidateFromParticipant(Context.ConnectionId, candidate);
        }
    }
}