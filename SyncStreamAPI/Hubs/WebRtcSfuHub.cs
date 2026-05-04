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
    // connectionId → roomId (populated when peer joins an SFU room)
    private static readonly ConcurrentDictionary<string, string> _sfuRoomByConnection = new();

    // connectionId → list of transportIds the peer owns
    private static readonly ConcurrentDictionary<string, List<string>> _sfuTransportsByConnection = new();

    // connectionId → list of producerIds the peer owns
    private static readonly ConcurrentDictionary<string, List<string>> _sfuProducersByConnection = new();

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

        _sfuRoomByConnection[Context.ConnectionId] = roomId;
        _sfuTransportsByConnection[Context.ConnectionId] = new List<string>();
        _sfuProducersByConnection[Context.ConnectionId] = new List<string>();

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
        _sfuTransportsByConnection.GetOrAdd(Context.ConnectionId, _ => new List<string>()).Add(info.Id);
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
        _sfuProducersByConnection.GetOrAdd(Context.ConnectionId, _ => new List<string>()).Add(producerId);

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
        if (_sfuProducersByConnection.TryGetValue(Context.ConnectionId, out var list))
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

    // ---------------------------------------------------------------
    // Disconnect cleanup (called from ServerHub.OnDisconnectedAsync)
    // ---------------------------------------------------------------

    internal async Task OnSfuDisconnectedAsync(string connectionId)
    {
        if (!_sfuRoomByConnection.TryRemove(connectionId, out var roomId))
            return;
        await CleanupSfuPeerAsync(connectionId, roomId);

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
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static string SfuGroupName(string roomId) => $"SFURoom-{roomId}";

    private async Task CleanupSfuPeerAsync(string connectionId, string roomId)
    {
        // Close all producers owned by this peer and notify the room.
        if (_sfuProducersByConnection.TryRemove(connectionId, out var producers))
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
        if (_sfuTransportsByConnection.TryRemove(connectionId, out var transports))
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

        _sfuRoomByConnection.TryRemove(connectionId, out _);
        await Groups.RemoveFromGroupAsync(connectionId, SfuGroupName(roomId));
    }

    // Resolved from the injected field on ServerHub.
    private WebRtcSfuManager SfuManager => _webRtcSfuManager;
}
