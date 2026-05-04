using System.Collections.Concurrent;
using System.Threading.Tasks;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    // Maps connectionId → roomId so OnDisconnectedAsync can notify peers.
    private static readonly ConcurrentDictionary<string, string> _voipRoomByConnection = new();

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task JoinAudioRoom(string token, string roomId)
    {
        var user = await MainManager.GetUser(token);
        if (user == null) return;

        // If already in another room, leave it first.
        if (_voipRoomByConnection.TryGetValue(Context.ConnectionId, out var existingRoom) &&
            existingRoom != roomId)
            await LeaveAudioRoomInternal(Context.ConnectionId, existingRoom);

        _voipRoomByConnection[Context.ConnectionId] = roomId;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"AudioRoom-{roomId}");
        await Clients.GroupExcept($"AudioRoom-{roomId}", [Context.ConnectionId])
            .participantJoined(new VoipParticipantDto
                { ParticipantId = Context.ConnectionId, ParticipantName = user.username });
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task LeaveAudioRoom(string token, string roomId)
    {
        _voipRoomByConnection.TryRemove(Context.ConnectionId, out _);
        await LeaveAudioRoomInternal(Context.ConnectionId, roomId);
    }

    private async Task LeaveAudioRoomInternal(string connectionId, string roomId)
    {
        await Groups.RemoveFromGroupAsync(connectionId, $"AudioRoom-{roomId}");
        await Clients.GroupExcept($"AudioRoom-{roomId}", [connectionId])
            .participantLeft(new VoipParticipantDto { ParticipantId = connectionId });
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendStatusToParticipant(string token, VoipParticipantDto participantDto, string roomId)
    {
        participantDto.ParticipantId = Context.ConnectionId;
        await Clients.GroupExcept($"AudioRoom-{roomId}", [Context.ConnectionId])
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
    public async Task SendIceCandidateToParticipant(string token, string participantId, object candidate)
    {
        var user = await MainManager.GetUser(token);
        if (user == null) return;
        await Clients.Client(participantId).receiveIceCandidateFromParticipant(Context.ConnectionId, candidate);
    }
}