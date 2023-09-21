using System.Threading.Tasks;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task JoinSFUStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || room.CurrentStreamer?.Length == 0) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StartSFUStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;
        room.CurrentStreamer = Context.ConnectionId;
        await Clients.OthersInGroup(roomId).startSFUStream(Context.ConnectionId);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendOfferToSFU(string token, WebRtcClientOffer offer)
    {
        // Forward the offer to the SFU
        // The SFU will then generate an answer and send it back to the client
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StopSFUStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || room.CurrentStreamer?.Length == 0) return;
        await Clients.OthersInGroup(roomId).stopSFUStream(room.CurrentStreamer);
        room.CurrentStreamer = null;
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendAnswerToSFU(string token, WebRtcClientOffer answer)
    {
        // Forward the answer to the SFU
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendIceCandidateToSFU(string token, WebRtcIceCandidate iceCandidate)
    {
        // Forward the ICE candidate to the SFU
    }
}