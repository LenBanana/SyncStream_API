using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json.Linq;
using SyncStreamAPI.Auth;
using SyncStreamAPI.Helper.Streaming;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Helper;

namespace SyncStreamAPI.Hubs;

/// <summary>
///     Signalling for the mediasoup SFU, and the room stage and member-list live streams that run on it.
/// </summary>
/// <remarks>
///     Every SFU room belongs to a chat room, and only that room's members may join it. Watching needs nothing
///     more, so guests can watch; sending needs an approved account and the right to send in that particular
///     SFU room: the stage's current holder, the owner of a live stream, or a participant in the call. Transports
///     and producers can only be used by the connection that created them, which the registry tracks.
/// </remarks>
public partial class ServerHub
{
    // ---------------------------------------------------------------
    // Access
    // ---------------------------------------------------------------

    /// <summary>The room, if the caller is a member of it; otherwise the call is refused.</summary>
    private Room RequireMember(string roomId)
    {
        var room = GetRoom(roomId);
        if (room?.server.FindByConnection(Context.ConnectionId) == null)
            throw new HubException("You are not in this room.");
        return room;
    }

    private SfuRoomId ParseSfuRoom(string sfuRoomId)
    {
        if (!SfuRoomId.TryParse(sfuRoomId, out var id))
            throw new HubException("Unknown media room.");
        return id;
    }

    /// <summary>Why the caller may not send into this SFU room, or null when they may.</summary>
    private string? SendRefusal(Room room, SfuRoomId id)
    {
        if (!Context.GetCaller().HasAtLeast(UserPrivileges.Approved))
            return "Sending audio or video needs an approved account.";

        var caller = Context.ConnectionId;
        switch (id.Kind)
        {
            case SfuRoomKind.Stage:
                // A file the server streams has no sender in the room; a browser share and a broadcast do.
                return room.CurrentStreamer == caller && room.IsStreamingSfu && !room.IsServerFileShare
                    ? null
                    : "You are not on this room's stage.";
            case SfuRoomKind.Live:
                bool isLive;
                lock (room.LiveStreamers)
                    isLive = room.LiveStreamers.Contains(caller);
                return id.StreamerId == caller && isLive ? null : "This is not your live stream.";
            default:
                return _voip.IsIn(caller, room.uniqueId) ? null : "You are not in this call.";
        }
    }

    /// <summary>Why the caller may not take the room's stage, or null when they may.</summary>
    private static string? StageRefusal(Room room, string connectionId)
    {
        if (room.IsRtmpFileShareActive) return "A file is already being shared to this room.";
        if (room.IsFileSharingActive) return "Someone is already sharing a file in this room.";
        if (room.CurrentStreamer != null && room.CurrentStreamer != connectionId)
            return "Someone else is already broadcasting to this room.";
        return null;
    }

    /// <summary>
    ///     Ends everything this connection was doing with media in this room. Used when it leaves the room and
    ///     when its connection drops.
    /// </summary>
    private async Task ReleaseMediaAsync(Room room, string connectionId)
    {
        if (room.IsRtmpFileShareActive && room.RtmpFileShareInitiator == connectionId)
            await StopRtmpFileShareInternalAsync(room);

        await _media.ReleaseConnectionInRoomAsync(room, connectionId);
    }

    private static string SfuGroupName(string sfuRoomId) => RoomMediaService.SfuGroupName(sfuRoomId);

    // ---------------------------------------------------------------
    // SFU plumbing
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns the mediasoup router's RTP capabilities.
    /// Clients must call this first and pass the result to <c>device.load()</c>.
    /// </summary>
    public async Task<object> GetRouterRtpCapabilities()
    {
        if (!MainManager.GetRooms().Any(room => room.server.FindByConnection(Context.ConnectionId) != null))
            throw new HubException("Join a room first.");
        return await SfuManager.GetRtpCapabilitiesAsync();
    }

    /// <summary>
    /// Joins an SFU room and returns the producers already in it, so the caller can start consuming them.
    /// Producers that appear later are announced with <c>sfuNewProducer</c>.
    /// </summary>
    public async Task<List<SfuProducerEntry>> JoinSfuRoom(string sfuRoomId)
    {
        var id = ParseSfuRoom(sfuRoomId);
        var room = RequireMember(id.RoomId);

        switch (id.Kind)
        {
            case SfuRoomKind.Live:
                bool isLive;
                lock (room.LiveStreamers)
                    isLive = room.LiveStreamers.Contains(id.StreamerId!);
                if (!isLive) throw new HubException("That stream has ended.");
                break;
            case SfuRoomKind.Voice:
                if (!_voip.IsIn(Context.ConnectionId, room.uniqueId))
                    throw new HubException("Join the call first.");
                break;
        }

        await SfuManager.EnsureRoomAsync(sfuRoomId);
        _sfuPeers.Join(Context.ConnectionId, sfuRoomId);
        await Groups.AddToGroupAsync(Context.ConnectionId, SfuGroupName(sfuRoomId));
        return await SfuManager.GetProducersAsync(sfuRoomId);
    }

    /// <summary>Leaves the SFU room and closes everything the caller had in it.</summary>
    public async Task LeaveSfuRoom(string sfuRoomId)
    {
        await _media.ReleaseSfuAsync(Context.ConnectionId, candidate => candidate == sfuRoomId);
    }

    /// <summary>
    /// Creates a WebRTC send or receive transport in a joined SFU room and returns what the client needs for
    /// <c>device.createSendTransport()</c> or <c>device.createRecvTransport()</c>.
    /// </summary>
    public async Task<SfuTransportInfo> CreateWebRtcTransport(string sfuRoomId, SfuTransportDirection direction)
    {
        var id = ParseSfuRoom(sfuRoomId);
        var room = RequireMember(id.RoomId);
        if (!_sfuPeers.HasJoined(Context.ConnectionId, sfuRoomId))
            throw new HubException("Join the media room first.");

        var sending = direction == SfuTransportDirection.Send;
        if (sending && SendRefusal(room, id) is { } refusal)
            throw new HubException(refusal);

        var info = await SfuManager.CreateTransportAsync(sfuRoomId);
        _sfuPeers.AddTransport(Context.ConnectionId, sfuRoomId, info.Id, sending);
        return info;
    }

    /// <summary>
    /// Finalises the DTLS handshake so media can flow.
    /// Called by the client after <c>transport.on('connect', …)</c> fires.
    /// </summary>
    public async Task ConnectTransport(string sfuRoomId, string transportId, JObject dtlsParameters)
    {
        if (!_sfuPeers.OwnsTransport(Context.ConnectionId, sfuRoomId, transportId))
            throw new HubException("Unknown transport.");
        await SfuManager.ConnectTransportAsync(sfuRoomId, transportId, dtlsParameters);
    }

    /// <summary>New ICE credentials for one of the caller's transports whose connection failed.</summary>
    public async Task<JObject> RestartSfuIce(string sfuRoomId, string transportId)
    {
        if (!_sfuPeers.OwnsTransport(Context.ConnectionId, sfuRoomId, transportId))
            throw new HubException("Unknown transport.");
        return await SfuManager.RestartIceAsync(sfuRoomId, transportId);
    }

    /// <summary>
    /// Registers a new media producer on the caller's send transport, announces it to everyone else in the
    /// SFU room, and returns its id.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task<string> Produce(string sfuRoomId, string transportId, string kind, JObject rtpParameters)
    {
        var id = ParseSfuRoom(sfuRoomId);
        var room = RequireMember(id.RoomId);
        if (!_sfuPeers.OwnsSendTransport(Context.ConnectionId, sfuRoomId, transportId))
            throw new HubException("Unknown transport.");
        if (SendRefusal(room, id) is { } refusal)
            throw new HubException(refusal);
        if (!_sfuPeers.CanAddProducer(Context.ConnectionId, sfuRoomId))
            throw new HubException("Too many tracks.");

        var producerId = await SfuManager.ProduceAsync(sfuRoomId, transportId, kind, rtpParameters, Context.ConnectionId);
        _sfuPeers.AddProducer(Context.ConnectionId, sfuRoomId, producerId);

        await Clients.GroupExcept(SfuGroupName(sfuRoomId), new[] { Context.ConnectionId })
            .sfuNewProducer(new SfuNewProducerNotification
            {
                ProducerId = producerId,
                PeerId = Context.ConnectionId,
                Kind = kind,
                RoomId = sfuRoomId
            });

        return producerId;
    }

    /// <summary>Closes one of the caller's producers (e.g. when the camera is switched off).</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task CloseProducer(string sfuRoomId, string producerId)
    {
        if (!_sfuPeers.RemoveProducer(Context.ConnectionId, sfuRoomId, producerId))
            throw new HubException("Unknown producer.");

        await SfuManager.CloseProducerAsync(sfuRoomId, producerId);
        await Clients.GroupExcept(SfuGroupName(sfuRoomId), new[] { Context.ConnectionId })
            .sfuProducerClosed(sfuRoomId, producerId);
    }

    /// <summary>
    /// Creates a consumer for <paramref name="producerId"/> on the caller's recv transport.
    /// Returns consumer parameters for use with <c>recvTransport.consume()</c>.
    /// </summary>
    public async Task<SfuConsumerInfo> Consume(
        string sfuRoomId, string transportId, string producerId, JObject rtpCapabilities)
    {
        if (!_sfuPeers.OwnsRecvTransport(Context.ConnectionId, sfuRoomId, transportId))
            throw new HubException("Unknown transport.");
        return await SfuManager.ConsumeAsync(sfuRoomId, transportId, producerId, rtpCapabilities, Context.ConnectionId);
    }

    /// <summary>Returns true when the mediasoup SFU is reachable.</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task<bool> IsSfuAvailable()
    {
        return await SfuManager.IsHealthyAsync();
    }

    // ---------------------------------------------------------------
    // Room stage broadcast
    // ---------------------------------------------------------------

    /// <summary>
    /// Takes the room's stage for the caller's screen or camera. Everyone's player switches to the stage and
    /// joins its SFU room; the caller then produces into it.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task StartSfuStream(string roomId)
    {
        var room = RequireMember(roomId);
        if (!CheckPrivileges(room))
            throw new HubException("You don't have permission to broadcast in this room.");

        lock (room.StageGate)
        {
            if (StageRefusal(room, Context.ConnectionId) is { } refusal)
                throw new HubException(refusal);
            room.CurrentStreamer = Context.ConnectionId;
            room.IsStreamingSfu = true;
        }

        await Clients.GroupExcept(room.uniqueId, new[] { Context.ConnectionId })
            .startSfuStream(roomId, Context.ConnectionId);
        await RoomManager.SendPlayerType(room);
    }

    /// <summary>Ends the caller's broadcast and puts the room's player back to what it was showing.</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task StopSfuStream(string roomId)
    {
        var room = RequireMember(roomId);
        if (room.CurrentStreamer != Context.ConnectionId || !room.IsBroadcastOnStage) return;

        await _media.EndBroadcastAsync(room);
        await _media.ReleaseSfuAsync(Context.ConnectionId, candidate => candidate == SfuRoomId.Stage(roomId));
    }

    // ---------------------------------------------------------------
    // Member-list live streams
    // ---------------------------------------------------------------

    /// <summary>
    /// Marks the caller as live in the member list and returns the SFU room to send into. Nobody is switched
    /// to it; members choose to watch.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task<string> StartSfuOptInStream(string roomId)
    {
        var room = RequireMember(roomId);

        lock (room.LiveStreamers)
            room.LiveStreamers.Add(Context.ConnectionId);

        var member = room.server.FindByConnection(Context.ConnectionId);
        if (member != null) member.IsStreaming = true;

        await Clients.Group(room.uniqueId)
            .userupdate(room.server.members.Select(x => x?.ToDTO()).ToList());

        return SfuRoomId.Live(roomId, Context.ConnectionId);
    }

    /// <summary>Ends the caller's live stream and sends its viewers away.</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task StopSfuOptInStream(string roomId)
    {
        var room = RequireMember(roomId);
        await _media.EndLiveStreamAsync(room, Context.ConnectionId);
    }

    // ---------------------------------------------------------------
    // Recording
    // ---------------------------------------------------------------

    /// <summary>Records the caller's broadcast on the room's stage to a file on the server.</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task StartRecording(string roomId)
    {
        var room = RequireMember(roomId);
        if (room.CurrentStreamer != Context.ConnectionId || !room.IsBroadcastOnStage)
            throw new HubException("Only the broadcaster can record the stage.");
        if (room.ActiveRecording != null)
            throw new HubException("The stage is already being recorded.");

        var info = await SfuManager.StartRecordingAsync(roomId, SfuRoomId.Stage(roomId));
        room.ActiveRecording = info;
        await Clients.Group(SfuGroupName(SfuRoomId.Stage(roomId))).sfuRecordingStarted(info);
    }

    /// <summary>Stops recording the stage; everyone watching is told the file it was saved to.</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task StopRecording(string roomId)
    {
        var room = RequireMember(roomId);
        if (room.CurrentStreamer != Context.ConnectionId || room.ActiveRecording == null) return;

        room.ActiveRecording = null;
        var filename = await SfuManager.StopRecordingAsync(SfuRoomId.Stage(roomId));
        await Clients.Group(SfuGroupName(SfuRoomId.Stage(roomId))).sfuRecordingStopped(roomId, filename);
    }

    // Resolved from the injected field on ServerHub.
    private WebRtcSfuManager SfuManager => _webRtcSfuManager;
}
