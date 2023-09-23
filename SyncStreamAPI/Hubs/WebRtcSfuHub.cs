using System.Threading.Tasks;
using Org.WebRtc;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task JoinSFURoom(string token, string roomId)
    {
        var user = await MainManager.GetUser(token);
        if (user == null) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"AudioRoom-{roomId}");
        await Clients.GroupExcept($"AudioRoom-{roomId}", new[] { Context.ConnectionId })
            .participantJoined(new VoipParticipantDto()
                { ParticipantId = Context.ConnectionId, ParticipantName = user.username });
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task LeaveSFURoom(string token, string roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"AudioRoom-{roomId}");
        await Clients.GroupExcept($"AudioRoom-{roomId}", new[] { Context.ConnectionId })
            .participantLeft(new VoipParticipantDto() { ParticipantId = Context.ConnectionId });
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendStatusToSFUParticipant(string token, VoipParticipantDto participantDto, string roomId)
    {
        participantDto.ParticipantId = Context.ConnectionId;
        await Clients.GroupExcept($"AudioRoom-{roomId}", new[] { Context.ConnectionId })
            .receiveStatusFromParticipant(participantDto);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendOfferToSFUParticipant(string token, string participantId, VoipOffer offer)
    {
        var user = await MainManager.GetUser(token);
        if (user == null) return;
        offer.ParticipantName = user.username;
        await Clients.Client(participantId).receiveOfferFromParticipant(Context.ConnectionId, offer);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendAnswerToSFUParticipant(string token, string participantId, VoipOffer answer)
    {
        var user = await MainManager.GetUser(token);
        if (user == null) return;
        answer.ParticipantName = user.username;
        await Clients.Client(participantId).receiveAnswerFromParticipant(Context.ConnectionId, answer);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendIceCandidateToSFUParticipant(string token, string participantId, VoipIceCandidate candidate)
    {
        await Clients.Client(participantId).receiveIceCandidateFromParticipant(Context.ConnectionId, candidate);
    }
}