using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Helper;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    // connectionId → set of SFU roomIds the peer currently participates in.
    private static readonly ConcurrentDictionary<string, HashSet<string>> _sfuRoomsByConnection = new();

    // composite key `${connectionId}::{roomId}` → list of transportIds the peer owns in that SFU room.
    private static readonly ConcurrentDictionary<string, List<string>> _sfuTransportsByPeerRoom = new();

    // composite key `${connectionId}::{roomId}` → list of producerIds the peer owns in that SFU room.
    private static readonly ConcurrentDictionary<string, List<string>> _sfuProducersByPeerRoom = new();

    // ---------------------------------------------------------------
    // Capabilities
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns the mediasoup router's RTP capabilities.
    /// Clients must call this first and pass the result to <c>device.load()</c>.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task<object> GetRouterRtpCapabilities(string token)
    {
        return await SfuManager.GetRtpCapabilitiesAsync();
    }

    // ---------------------------------------------------------------
    // Room
    // ---------------------------------------------------------------

    /// <summary>
    /// Joins the SFU room.  Creates the room on the mediasoup server if needed,
    /// then returns the list of currently-active producers so the caller can
    /// immediately start consuming them.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task<List<SfuProducerEntry>> JoinSfuRoom(string token, string roomId)
    {
        await SfuManager.EnsureRoomAsync(roomId);

        var joinedRooms = _sfuRoomsByConnection.GetOrAdd(Context.ConnectionId, _ => new HashSet<string>());
        lock (joinedRooms)
            joinedRooms.Add(roomId);

        _sfuTransportsByPeerRoom.TryAdd(SfuPeerRoomKey(Context.ConnectionId, roomId), new List<string>());
        _sfuProducersByPeerRoom.TryAdd(SfuPeerRoomKey(Context.ConnectionId, roomId), new List<string>());

        await Groups.AddToGroupAsync(Context.ConnectionId, SfuGroupName(roomId));
        return await SfuManager.GetProducersAsync(roomId);
    }

    /// <summary>Leaves the SFU room and cleans up all server-side resources owned by this peer.</summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task LeaveSfuRoom(string token, string roomId)
    {
        await CleanupSfuPeerAsync(Context.ConnectionId, roomId);
    }

    // ---------------------------------------------------------------
    // Transport
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a WebRTC send or receive transport in the specified room.
    /// Returns the transport parameters that the client passes to
    /// <c>device.createSendTransport()</c> or <c>device.createRecvTransport()</c>.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task<SfuTransportInfo> CreateWebRtcTransport(
        string token, string roomId, SfuTransportDirection direction)
    {
        var producing = direction == SfuTransportDirection.Send;
        var consuming = direction == SfuTransportDirection.Recv;
        var info = await SfuManager.CreateTransportAsync(roomId, producing, consuming);
        _sfuTransportsByPeerRoom.GetOrAdd(SfuPeerRoomKey(Context.ConnectionId, roomId), _ => new List<string>()).Add(info.Id);
        return info;
    }

    /// <summary>
    /// Finalises the DTLS handshake so media can flow.
    /// Called by the client after <c>transport.on('connect', …)</c> fires.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task ConnectTransport(string token, string roomId, string transportId, JObject dtlsParameters)
    {
        await SfuManager.ConnectTransportAsync(roomId, transportId, dtlsParameters);
    }

    // ---------------------------------------------------------------
    // Producer
    // ---------------------------------------------------------------

    /// <summary>
    /// Registers a new media producer on the send transport.
    /// Notifies all other peers in the room so they can consume this track.
    /// Returns the producer ID.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task<string> Produce(
        string token, string roomId, string transportId, string kind, JObject rtpParameters)
    {
        var producerId = await SfuManager.ProduceAsync(roomId, transportId, kind, rtpParameters, Context.ConnectionId);
        _sfuProducersByPeerRoom.GetOrAdd(SfuPeerRoomKey(Context.ConnectionId, roomId), _ => new List<string>()).Add(producerId);

        // Notify every other peer so they can consume the new track.
        await Clients.GroupExcept(SfuGroupName(roomId), new[] { Context.ConnectionId })
            .sfuNewProducer(new SfuNewProducerNotification
            {
                ProducerId = producerId,
                PeerId = Context.ConnectionId,
                Kind = kind,
                RoomId = roomId
            });

        return producerId;
    }

    /// <summary>Closes a specific producer (e.g. when the streamer pauses a track).</summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task CloseProducer(string token, string roomId, string producerId)
    {
        if (_sfuProducersByPeerRoom.TryGetValue(SfuPeerRoomKey(Context.ConnectionId, roomId), out var list))
            list.Remove(producerId);

        await SfuManager.CloseProducerAsync(roomId, producerId);
        await Clients.GroupExcept(SfuGroupName(roomId), new[] { Context.ConnectionId })
            .sfuProducerClosed(producerId);
    }

    // ---------------------------------------------------------------
    // Consumer
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a consumer for <paramref name="producerId"/> on the caller's recv transport.
    /// Returns consumer parameters for use with <c>recvTransport.consume()</c>.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task<SfuConsumerInfo> Consume(
        string token, string roomId, string transportId, string producerId, JObject rtpCapabilities)
    {
        return await SfuManager.ConsumeAsync(roomId, transportId, producerId, rtpCapabilities, Context.ConnectionId);
    }

    // ---------------------------------------------------------------
    // Health
    // ---------------------------------------------------------------

    /// <summary>Returns true when the mediasoup SFU is reachable.</summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task<bool> IsSfuAvailable(string token)
    {
        return await SfuManager.IsHealthyAsync();
    }

    // ---------------------------------------------------------------
    // Consumer preferred layers (simulcast / ABR)
    // ---------------------------------------------------------------

    /// <summary>
    /// Sets the preferred simulcast spatial and temporal layers for an existing consumer.
    /// Call from the viewer to lower/raise quality (e.g. based on available bandwidth).
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task SetPreferredLayers(string token, string roomId, string consumerId, int spatialLayer, int temporalLayer)
    {
        await SfuManager.SetPreferredLayersAsync(roomId, consumerId, spatialLayer, temporalLayer);
    }

    // ---------------------------------------------------------------
    // Recording
    // ---------------------------------------------------------------

    /// <summary>
    /// Starts recording all media producers in the SFU room to a server-side file.
    /// Notifies every peer in the room so the UI can show a recording indicator.
    /// Only the room host (or admin) should call this; the hub enforces that the caller is in the room.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StartRecording(string token, string roomId)
    {
        // Only the active SFU streamer for this room may start a recording.
        var room = MainManager.GetRoom(roomId);
        if (room == null || room.CurrentStreamer != Context.ConnectionId) return;
        var info = await SfuManager.StartRecordingAsync(roomId);
        await Clients.Group(SfuGroupName(roomId)).sfuRecordingStarted(info);
    }

    /// <summary>
    /// Stops the active recording in the SFU room.
    /// Notifies every peer in the room.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StopRecording(string token, string roomId)
    {
        // Only the active SFU streamer for this room may stop the recording.
        var room = MainManager.GetRoom(roomId);
        if (room == null || room.CurrentStreamer != Context.ConnectionId) return;
        var filename = await SfuManager.StopRecordingAsync(roomId);
        await Clients.Group(SfuGroupName(roomId)).sfuRecordingStopped(roomId);
    }

    // ---------------------------------------------------------------
    // Screen-share stream (SFU path for the main room player)
    // ---------------------------------------------------------------

    /// <summary>
    /// Called by the streamer after they have already joined the SFU room and produced tracks.
    /// Marks the room as using SFU streaming and notifies viewers to join via SFU.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StartSfuStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;

        // Stop any existing P2P stream first.
        if (!string.IsNullOrEmpty(room.CurrentStreamer) && !room.IsStreamingSfu)
            await StopWebRtcStream(token, roomId);

        room.CurrentStreamer = Context.ConnectionId;
        room.IsStreamingSfu = true;

        await Clients.GroupExcept(room.uniqueId, new[] { Context.ConnectionId })
            .startSfuStream(roomId, Context.ConnectionId);
        await RoomManager.SendPlayerType(room);
    }

    /// <summary>
    /// Stops the SFU stream for the room and resets its player type.
    /// Producers and transports are cleaned up when the streamer calls <c>LeaveSfuRoom</c>.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StopSfuStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || room.CurrentStreamer != Context.ConnectionId) return;

        room.CurrentStreamer = null;
        room.IsStreamingSfu = false;

        await Clients.Group(room.uniqueId).stopWebRtcStream(Context.ConnectionId);
        await RoomManager.SendPlayerType(room);
    }

    /// <summary>
    /// Starts an opt-in live screen-share routed through the SFU.
    /// Unlike <see cref="StartSfuStream"/>, this does not affect the room's main
    /// player or auto-join any viewers; it only marks the member as live in the user list.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StartSfuOptInStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;

        lock (room.ActiveStreamers)
            room.ActiveStreamers.Add(Context.ConnectionId);
        lock (room.ActiveSfuStreamers)
            room.ActiveSfuStreamers.Add(Context.ConnectionId);

        var member = room.server.members.Find(m => m?.ConnectionId == Context.ConnectionId);
        if (member != null) member.IsStreaming = true;

        await Clients.Group(room.uniqueId)
            .userupdate(room.server.members.ConvertAll(x => x?.ToDTO()));
    }

    /// <summary>
    /// Stops the caller's opt-in SFU live screen-share and dismisses any viewers
    /// currently watching via the popout/live-watch path.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StopSfuOptInStream(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;

        bool wasStreaming;
        lock (room.ActiveSfuStreamers)
            wasStreaming = room.ActiveSfuStreamers.Remove(Context.ConnectionId);
        if (!wasStreaming) return;

        lock (room.ActiveStreamers)
            room.ActiveStreamers.Remove(Context.ConnectionId);

        var member = room.server.members.Find(m => m?.ConnectionId == Context.ConnectionId);
        if (member != null) member.IsStreaming = false;

        await Clients.Group(room.uniqueId)
            .userupdate(room.server.members.ConvertAll(x => x?.ToDTO()));
        await Clients.Group(room.uniqueId).stopWebRtcStream(Context.ConnectionId);
    }

    /// <summary>
    /// Returns true when the specified streamer is currently sharing an opt-in live
    /// screen-share through the SFU rather than the legacy P2P watcher path.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public Task<bool> IsSfuOptInStreamActive(string token, string roomId, string streamerId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return Task.FromResult(false);

        lock (room.ActiveSfuStreamers)
            return Task.FromResult(room.ActiveSfuStreamers.Contains(streamerId));
    }

    // ---------------------------------------------------------------
    // Disconnect cleanup (called from ServerHub.OnDisconnectedAsync)
    // ---------------------------------------------------------------

    internal async Task OnSfuDisconnectedAsync(string connectionId)
    {
        if (!_sfuRoomsByConnection.TryGetValue(connectionId, out var roomIds))
            return;

        List<string> joinedRooms;
        lock (roomIds)
            joinedRooms = new List<string>(roomIds);

        foreach (var roomId in joinedRooms)
            await CleanupSfuPeerAsync(connectionId, roomId);

        _sfuRoomsByConnection.TryRemove(connectionId, out _);

        // If this peer was the active SFU streamer for any room, clean up.
        var rooms = MainManager.GetRooms();
        foreach (var room in rooms)
        {
            if (room.CurrentStreamer == connectionId && room.IsStreamingSfu)
            {
                room.CurrentStreamer = null;
                room.IsStreamingSfu = false;
                await Clients.Group(room.uniqueId).stopWebRtcStream(connectionId);
                await RoomManager.SendPlayerType(room);
            }

            bool stoppedOptInStream;
            lock (room.ActiveSfuStreamers)
                stoppedOptInStream = room.ActiveSfuStreamers.Remove(connectionId);

            if (!stoppedOptInStream) continue;

            lock (room.ActiveStreamers)
                room.ActiveStreamers.Remove(connectionId);

            var member = room.server.members.Find(m => m?.ConnectionId == connectionId);
            if (member != null) member.IsStreaming = false;

            await Clients.Group(room.uniqueId)
                .userupdate(room.server.members.ConvertAll(x => x?.ToDTO()));
            await Clients.Group(room.uniqueId).stopWebRtcStream(connectionId);
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static string SfuGroupName(string roomId) => $"SFURoom-{roomId}";
    private static string SfuPeerRoomKey(string connectionId, string roomId) => $"{connectionId}::{roomId}";

    private async Task CleanupSfuPeerAsync(string connectionId, string roomId)
    {
        var peerRoomKey = SfuPeerRoomKey(connectionId, roomId);

        // Close all producers owned by this peer and notify the room.
        if (_sfuProducersByPeerRoom.TryRemove(peerRoomKey, out var producers))
        {
            foreach (var pid in producers)
            {
                try
                {
                    await SfuManager.CloseProducerAsync(roomId, pid);
                    await Clients.GroupExcept(SfuGroupName(roomId), new[] { connectionId })
                        .sfuProducerClosed(pid);
                }
                catch
                {
                    // Best-effort; producer may already be gone.
                }
            }
        }

        // Close all transports owned by this peer.
        if (_sfuTransportsByPeerRoom.TryRemove(peerRoomKey, out var transports))
        {
            foreach (var tid in transports)
            {
                try
                {
                    await SfuManager.CloseTransportAsync(roomId, tid);
                }
                catch
                {
                    // Best-effort.
                }
            }
        }

        if (_sfuRoomsByConnection.TryGetValue(connectionId, out var roomIds))
        {
            lock (roomIds)
                roomIds.Remove(roomId);
        }

        await Groups.RemoveFromGroupAsync(connectionId, SfuGroupName(roomId));
    }

    // Resolved from the injected field on ServerHub.
    private WebRtcSfuManager SfuManager => _webRtcSfuManager;
}
