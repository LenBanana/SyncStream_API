using System.Threading.Tasks;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Helper;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task JoinWebRtcStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || string.IsNullOrEmpty(room.CurrentStreamer)) return;
        if (room.CurrentStreamer == Context.ConnectionId) return;
        await Clients.Client(room.CurrentStreamer).joinWebRtcStream(Context.ConnectionId);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StartWebRtcStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;
        if (room.CurrentStreamer != null) await StopWebRtcStream(token, roomId);

        room.CurrentStreamer = Context.ConnectionId;
        await Clients.GroupExcept(room.uniqueId, [Context.ConnectionId])
            .startWebRtcStream(Context.ConnectionId, roomId);
    }

    /// <summary>
    /// Moderator-only: forces all room members to join the caller's P2P stream.
    /// The caller must already have a local stream set up; this notifies every
    /// member with a <c>broadcastStream</c> event so their clients auto-join.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Moderator, AuthenticationType = AuthenticationType.Token)]
    public async Task ForceBroadcast(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;
        if (room.CurrentStreamer != null && room.CurrentStreamer != Context.ConnectionId)
            await StopWebRtcStream(token, roomId);

        room.CurrentStreamer = Context.ConnectionId;
        await Clients.GroupExcept(room.uniqueId, [Context.ConnectionId])
            .broadcastStream(Context.ConnectionId, roomId);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendOfferToViewer(string token, string connectionId, WebRtcClientOffer offer)
    {
        await Clients.Client(connectionId).sendOfferToViewer(offer);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StopWebRtcStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || string.IsNullOrEmpty(room.CurrentStreamer)) return;
        if (room.CurrentStreamer != Context.ConnectionId) return;
        await Clients.GroupExcept(room.uniqueId, [Context.ConnectionId])
            .stopWebRtcStream(room.CurrentStreamer);
        room.CurrentStreamer = null;
        await RoomManager.SendPlayerType(room);
        await Clients.Group(room.uniqueId).stopWebRtcStream(Context.ConnectionId);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task CreateStreamAnswer(string token, string roomId, WebRtcClientOffer answer)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || string.IsNullOrEmpty(room.CurrentStreamer)) return;
        if (room.CurrentStreamer == Context.ConnectionId) return;
        answer.ViewerId = Context.ConnectionId;
        await Clients.Client(room.CurrentStreamer).sendClientAnswer(answer);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendIceCandidate(string token, string roomId, object iceCandidate)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || string.IsNullOrEmpty(room.CurrentStreamer)) return;
        if (room.CurrentStreamer == Context.ConnectionId) return;
        // Include viewerId so the streamer can route the candidate to the correct RTCPeerConnection.
        await Clients.Client(room.CurrentStreamer).sendIceCandidateFromViewer(Context.ConnectionId, iceCandidate);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendIceCandidateToViewer(string token, string connectionId, object iceCandidate)
    {
        await Clients.Client(connectionId).sendIceCandidate(iceCandidate);
    }

    /// <summary>
    /// Called by a viewer when its ICE connection has failed and it needs the streamer to create a new offer
    /// with iceRestart:true so both sides re-gather candidates through the TURN server.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task RequestIceRestart(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || string.IsNullOrEmpty(room.CurrentStreamer)) return;
        if (room.CurrentStreamer == Context.ConnectionId) return;
        await Clients.Client(room.CurrentStreamer).joinWebRtcStream(Context.ConnectionId);
    }
}