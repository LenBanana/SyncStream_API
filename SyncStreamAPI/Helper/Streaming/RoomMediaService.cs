using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Helper;

namespace SyncStreamAPI.Helper.Streaming;

/// <summary>
///     How a room's media ends: a broadcast, a file share, a member's live stream, a seat in the voice call, and
///     the SFU resources behind each.
/// </summary>
/// <remarks>
///     Each of these used to be torn down in several places (an explicit stop, the disconnect handler, leaving
///     the room), and each place had its own idea of what tearing down meant. Leaving the room did none of it, so
///     a broadcaster who left kept the room's player pinned to a stream that no longer existed, and the
///     disconnect handler cleared the streamer before checking whether there was a broadcast to end, so a
///     broadcaster who lost their connection did the same. Everything now ends through here, from a hub
///     call and from background work alike.
/// </remarks>
public class RoomMediaService
{
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly WebRtcSfuManager _sfu;
    private readonly SfuPeerRegistry _peers;
    private readonly VoipRoster _voip;

    public RoomMediaService(IHubContext<ServerHub, IServerHub> hub, WebRtcSfuManager sfu, SfuPeerRegistry peers,
        VoipRoster voip)
    {
        _hub = hub;
        _sfu = sfu;
        _peers = peers;
        _voip = voip;
    }

    public static string SfuGroupName(string sfuRoomId) => $"SFURoom-{sfuRoomId}";

    /// <summary>Ends everything this connection was doing with media in this room.</summary>
    public async Task ReleaseConnectionInRoomAsync(Room room, string connectionId)
    {
        if (room.IsFileSharingActive && room.FileShareInitiator == connectionId)
            await StopFileShareAsync(room);

        if (room.CurrentStreamer == connectionId && room.IsBroadcastOnStage)
            await EndBroadcastAsync(room);

        await EndLiveStreamAsync(room, connectionId);

        if (_voip.IsIn(connectionId, room.uniqueId))
            await LeaveVoiceAsync(connectionId);

        await ReleaseSfuAsync(connectionId, sfuRoomId =>
            SfuRoomId.TryParse(sfuRoomId, out var id) && id.RoomId == room.uniqueId);
    }

    /// <summary>
    ///     Ends the broadcast on the room's stage, stopping its recording, and puts the room's player back to
    ///     whatever it was showing before.
    /// </summary>
    public async Task EndBroadcastAsync(Room room)
    {
        string? streamer;
        SfuRecordingInfo? recording;
        lock (room.StageGate)
        {
            if (!room.IsBroadcastOnStage) return;
            streamer = room.CurrentStreamer;
            recording = room.ActiveRecording;
            room.CurrentStreamer = null;
            room.IsStreamingSfu = false;
            room.ActiveRecording = null;
        }

        if (recording != null)
        {
            var filename = recording.Filename;
            await BestEffortAsync(async () => filename = await _sfu.StopRecordingAsync(SfuRoomId.Stage(room.uniqueId)),
                $"stopping the recording in room {room.uniqueId}");
            await _hub.Clients.Group(SfuGroupName(SfuRoomId.Stage(room.uniqueId)))
                .sfuRecordingStopped(room.uniqueId, filename);
        }

        await _hub.Clients.Group(room.uniqueId).stopWebRtcStream(streamer!);
        await RoomManager.SendPlayerType(room);
    }

    /// <summary>Takes the connection out of the member list's live streams, if it was live.</summary>
    public async Task EndLiveStreamAsync(Room room, string connectionId)
    {
        bool wasLive;
        lock (room.LiveStreamers)
            wasLive = room.LiveStreamers.Remove(connectionId);
        if (!wasLive) return;

        var member = room.server.FindByConnection(connectionId);
        if (member != null) member.IsStreaming = false;

        await _hub.Clients.Group(room.uniqueId)
            .userupdate(room.server.members.Select(x => x?.ToDTO()).ToList());
        await _hub.Clients.Group(room.uniqueId).stopWebRtcStream(connectionId);
        await ReleaseSfuAsync(connectionId, sfuRoomId => sfuRoomId == SfuRoomId.Live(room.uniqueId, connectionId));
    }

    /// <summary>
    ///     Clears all file-share flags, notifies the room, and re-evaluates the player type so clients switch
    ///     back to whatever was playing before.
    /// </summary>
    public async Task StopFileShareAsync(Room room)
    {
        bool wasServerShare;
        string? uploadId;
        lock (room.StageGate)
        {
            if (!room.IsFileSharingActive) return;
            wasServerShare = room.IsServerFileShare;
            uploadId = room.FileShareUploadId;
            room.IsFileSharingActive = false;
            room.IsFileSharePaused = false;
            room.IsServerFileShare = false;
            room.FileShareInitiator = null;
            room.FileShareUploadId = null;
            room.FileShareUploadInProgress = false;
            room.CurrentStreamer = null;
            room.IsStreamingSfu = false;
        }

        if (wasServerShare)
        {
            var stage = SfuRoomId.Stage(room.uniqueId);
            await BestEffortAsync(async () =>
            {
                var status = await _sfu.GetServerFileStreamAsync(stage);
                if (!await _sfu.StopServerFileStreamAsync(stage) || status == null) return;
                foreach (var producerId in new[] { status.VideoProducerId, status.AudioProducerId })
                    if (!string.IsNullOrEmpty(producerId))
                        await _hub.Clients.Group(SfuGroupName(stage)).sfuProducerClosed(stage, producerId);
            }, $"stopping the server file stream in room {room.uniqueId}");
        }

        // Clean up the upload staging directory left by an early-stream upload. Tested for shape rather than
        // just for emptiness, because building the path throws on a malformed id and this runs while a room is
        // being torn down.
        if (RoomUploadIds.IsWellFormed(uploadId))
        {
            var uploadDir = RoomUploadPaths.DirectoryFor(uploadId!);
            try
            {
                if (Directory.Exists(uploadDir)) Directory.Delete(uploadDir, recursive: true);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[FileShare] could not delete upload dir {uploadDir}: {ex.Message}");
            }
        }

        await _hub.Clients.Group(room.uniqueId).fileShareStopped();
        await RoomManager.SendPlayerType(room);
    }

    /// <summary>Takes the connection out of its voice call and tells the others.</summary>
    public async Task LeaveVoiceAsync(string connectionId)
    {
        var roomId = _voip.Leave(connectionId);
        if (roomId == null) return;

        var voice = SfuRoomId.Voice(roomId);
        await ReleaseSfuAsync(connectionId, sfuRoomId => sfuRoomId == voice);
        // Joining the call puts the connection in the group before it joins the SFU room, so leaving the SFU
        // room alone does not always take it out.
        await _hub.Groups.RemoveFromGroupAsync(connectionId, SfuGroupName(voice));
        await _hub.Clients.Group(SfuGroupName(voice))
            .participantLeft(new VoipParticipantDto { ParticipantId = connectionId });
    }

    /// <summary>
    ///     Closes the connection's producers and transports in the SFU rooms matching the filter, tells the rest
    ///     of each room which producers went away, and takes the connection out of the rooms' groups.
    /// </summary>
    public async Task ReleaseSfuAsync(string connectionId, Func<string, bool> sfuRoomFilter)
    {
        foreach (var resources in _peers.LeaveWhere(connectionId, sfuRoomFilter))
        {
            var group = SfuGroupName(resources.SfuRoomId);

            foreach (var producerId in resources.Producers)
            {
                await BestEffortAsync(() => _sfu.CloseProducerAsync(resources.SfuRoomId, producerId),
                    $"closing producer {producerId} in {resources.SfuRoomId}");
                await _hub.Clients.GroupExcept(group, new[] { connectionId })
                    .sfuProducerClosed(resources.SfuRoomId, producerId);
            }

            foreach (var transportId in resources.Transports)
                await BestEffortAsync(() => _sfu.CloseTransportAsync(resources.SfuRoomId, transportId),
                    $"closing transport {transportId} in {resources.SfuRoomId}");

            await _hub.Groups.RemoveFromGroupAsync(connectionId, group);
        }
    }

    /// <summary>
    ///     Runs one SFU call that is part of ending something. Ending has to finish even when the SFU is
    ///     unreachable or no longer has the resource: the room still needs telling, and one resource that will
    ///     not close must not keep the rest open. So a failure is logged and the teardown carries on; the SFU
    ///     itself closes whatever was left once a room has been idle for a minute.
    /// </summary>
    private static async Task BestEffortAsync(Func<Task> call, string what)
    {
        try
        {
            await call();
        }
        catch (SfuRequestException ex)
        {
            Console.WriteLine($"[SFU] {what} failed: {ex.Message}");
        }
    }
}
