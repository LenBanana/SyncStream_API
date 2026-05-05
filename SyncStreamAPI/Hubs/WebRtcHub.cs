using System.Linq;
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
    // ---------------------------------------------------------------
    // Discord-style P2P Streaming — anyone can stream, viewers opt in
    // ---------------------------------------------------------------

    /// <summary>
    /// Start a P2P screen-share stream.  Adds this connection to the room's
    /// <see cref="Models.Room.ActiveStreamers"/> set and notifies all other room
    /// members so they can show a streaming indicator in the user list.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StartWebRtcStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;

        lock (room.ActiveStreamers)
            room.ActiveStreamers.Add(Context.ConnectionId);

        // Mark the member as streaming so the updated list carries the flag.
        var member = room.server.members.FirstOrDefault(m => m?.ConnectionId == Context.ConnectionId);
        if (member != null) member.IsStreaming = true;

        // Push updated member list — viewers will see the live indicator.
        await Clients.Group(room.uniqueId)
            .userupdate(room.server.members.Select(x => x?.ToDTO()).ToList());

        // Notify room (for any listener that needs the streamerId).
        await Clients.GroupExcept(room.uniqueId, [Context.ConnectionId])
            .startWebRtcStream(Context.ConnectionId, roomId);
    }

    /// <summary>
    /// Stop the caller's P2P stream, release server-side state, and notify viewers.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StopWebRtcStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;

        bool wasStreaming;
        lock (room.ActiveStreamers)
            wasStreaming = room.ActiveStreamers.Remove(Context.ConnectionId);

        if (!wasStreaming) return;

        if (room.CurrentStreamer == Context.ConnectionId)
        {
            lock (room.ActiveStreamers)
                room.CurrentStreamer = room.ActiveStreamers.FirstOrDefault();
        }

        var member = room.server.members.FirstOrDefault(m => m?.ConnectionId == Context.ConnectionId);
        if (member != null) member.IsStreaming = false;

        await Clients.Group(room.uniqueId)
            .userupdate(room.server.members.Select(x => x?.ToDTO()).ToList());

        await Clients.Group(room.uniqueId).stopWebRtcStream(Context.ConnectionId);

        if (!room.ActiveStreamers.Any())
            await RoomManager.SendPlayerType(room);
    }

    /// <summary>
    /// Viewer calls this to request a P2P offer from a specific streamer.
    /// <paramref name="streamerId"/> is the streamer's SignalR connectionId.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task JoinWebRtcStream(string token, string roomId, string streamerId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;
        bool isActive;
        lock (room.ActiveStreamers)
            isActive = room.ActiveStreamers.Contains(streamerId);
        if (!isActive) return;
        if (streamerId == Context.ConnectionId) return;
        await Clients.Client(streamerId).joinWebRtcStream(Context.ConnectionId);
    }

    /// <summary>
    /// Streamer sends its SDP offer to the joining viewer.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendOfferToViewer(string token, string connectionId, WebRtcClientOffer offer)
    {
        await Clients.Client(connectionId).sendOfferToViewer(offer);
    }

    /// <summary>
    /// Viewer sends its SDP answer back to the streamer.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task CreateStreamAnswer(string token, string streamerId, WebRtcClientOffer answer)
    {
        var room = MainManager.GetRoom(answer.RoomId ?? "");
        bool isActive = room != null && room.ActiveStreamers.Contains(streamerId);
        if (!isActive) return;
        if (streamerId == Context.ConnectionId) return;
        answer.ViewerId = Context.ConnectionId;
        await Clients.Client(streamerId).sendClientAnswer(answer);
    }

    /// <summary>
    /// Viewer sends an ICE candidate to a specific streamer.
    /// <paramref name="streamerId"/> identifies which streamer's RTCPeerConnection to target.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendIceCandidate(string token, string roomId, string streamerId, object iceCandidate)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;
        bool isActive;
        lock (room.ActiveStreamers)
            isActive = room.ActiveStreamers.Contains(streamerId);
        if (!isActive || streamerId == Context.ConnectionId) return;
        await Clients.Client(streamerId).sendIceCandidateFromViewer(Context.ConnectionId, iceCandidate);
    }

    /// <summary>
    /// Streamer sends an ICE candidate to a specific viewer.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SendIceCandidateToViewer(string token, string connectionId, object iceCandidate)
    {
        await Clients.Client(connectionId).sendIceCandidate(iceCandidate);
    }

    /// <summary>
    /// Viewer requests an ICE restart from the streamer it is watching.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task RequestIceRestart(string token, string roomId, string streamerId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;
        bool isActive;
        lock (room.ActiveStreamers)
            isActive = room.ActiveStreamers.Contains(streamerId);
        if (!isActive || streamerId == Context.ConnectionId) return;
        await Clients.Client(streamerId).joinWebRtcStream(Context.ConnectionId);
    }

    // ---------------------------------------------------------------
    // Moderator force-broadcast (auto-joins all room members)
    // ---------------------------------------------------------------

    /// <summary>
    /// Moderator-only: forces all room members to join the caller's P2P stream.
    /// Sends a <c>broadcastStream</c> event so every client auto-connects.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Moderator, AuthenticationType = AuthenticationType.Token)]
    public async Task ForceBroadcast(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;

        lock (room.ActiveStreamers)
            room.ActiveStreamers.Add(Context.ConnectionId);
        room.CurrentStreamer = Context.ConnectionId;

        var member = room.server.members.FirstOrDefault(m => m?.ConnectionId == Context.ConnectionId);
        if (member != null) member.IsStreaming = true;

        await Clients.Group(room.uniqueId)
            .userupdate(room.server.members.Select(x => x?.ToDTO()).ToList());

        await Clients.GroupExcept(room.uniqueId, [Context.ConnectionId])
            .broadcastStream(Context.ConnectionId, roomId);
    }

    // ---------------------------------------------------------------
    // Helper: stop streaming on disconnect (called from OnDisconnectedAsync)
    // ---------------------------------------------------------------

    internal async Task StopWebRtcStreamOnDisconnect(string connectionId)
    {
        var rooms = MainManager.GetRooms();
        foreach (var room in rooms)
        {
            bool wasStreaming;
            lock (room.ActiveStreamers)
                wasStreaming = room.ActiveStreamers.Remove(connectionId);
            if (!wasStreaming) continue;

            if (room.CurrentStreamer == connectionId)
            {
                lock (room.ActiveStreamers)
                    room.CurrentStreamer = room.ActiveStreamers.FirstOrDefault();
            }

            var member = room.server.members.FirstOrDefault(m => m?.ConnectionId == connectionId);
            if (member != null) member.IsStreaming = false;

            await Clients.Group(room.uniqueId)
                .userupdate(room.server.members.Select(x => x?.ToDTO()).ToList());
            await Clients.Group(room.uniqueId).stopWebRtcStream(connectionId);

            if (!room.ActiveStreamers.Any())
                await RoomManager.SendPlayerType(room);
        }
    }
}