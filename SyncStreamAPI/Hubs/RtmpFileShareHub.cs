using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Helper.Streaming;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    // Field resolved via the injected RtmpFileShareManager; set in the ServerHub constructor.
    private RtmpFileShareManager _rtmpFileShareManager;

    // ── RTMP file-share — mirroring WebRtcFileShareHub pattern ────────────────

    /// <summary>
    /// Starts an RTMP file-share from a chunked upload already in progress.
    /// The upload.bin file is fed directly to ffmpeg which pushes RTMP to nginx-rtmp.
    /// Viewers and the room's Live modal immediately reflect the stream via the existing
    /// OBS/RTMP infrastructure (ON_PUBLISH fires automatically from nginx-rtmp).
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StartRtmpFileShareFromUpload(
        string token,
        string roomId,
        string uploadId,
        RtmpPlaybackPreferences? playbackPreferences = null)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;

        var member = room.server.members.FirstOrDefault(m => m?.ConnectionId == Context.ConnectionId);
        if (member == null) return;

        var dbUser = _postgres.Users?
            .Include(x => x.RememberTokens)
            .FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == token));
        if (dbUser == null) return;

        // Mutual exclusion: reject if user is already live via OBS.
        if (_manager.LiveUsers.Any(u => u.id == dbUser.StreamToken))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Warning)
            {
                Header   = "Already streaming",
                Question = "You are currently live-streaming via OBS. Stop the OBS stream first.",
                Answer1  = "Ok"
            });
            return;
        }

        if (room.IsFileSharingActive || room.IsRtmpFileShareActive)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Warning)
            {
                Header   = "File Share Unavailable",
                Question = "Someone is already sharing a file in this room. Please wait until they finish.",
                Answer1  = "Ok"
            });
            return;
        }

        var filePath = Path.Combine(General.TemporaryFilePath, "room-uploads", uploadId, "upload.bin");
        if (!File.Exists(filePath))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header   = "Upload Not Found",
                Question = "The upload session could not be found. Please try again.",
                Answer1  = "Ok"
            });
            return;
        }

        room.IsRtmpFileShareActive  = true;
        room.RtmpFileShareInitiator = Context.ConnectionId;
        room.RtmpFileShareUploadId  = uploadId;
        room.RtmpFileShareAssetKey  = uploadId;
        room.RtmpFileShareUsesVodPlayback = false;
        room.RtmpFileShareDurationSec = null;

        try
        {
            var session = await _rtmpFileShareManager.StartAsync(
                roomId, Context.ConnectionId, dbUser.ID, dbUser.username,
                dbUser.StreamToken, filePath, uploadId, playbackPreferences);

            var httpContext = _httpContextAccessor.HttpContext;
            var publicBaseUrl = General.GetPublicBaseUrl(httpContext?.Request, Configuration);
            var streamUrl = !string.IsNullOrWhiteSpace(publicBaseUrl)
                ? $"{publicBaseUrl}/api/rtmp/liveProxy?stream={Uri.EscapeDataString(dbUser.username.ToLower())}"
                : $"{(Configuration["LiveStreamBaseUrl"]?.TrimEnd('/') ?? "https://live.drecktu.be/live")}?stream={Uri.EscapeDataString(dbUser.username.ToLower())}";
            room.RtmpStreamUrl = streamUrl;
            room.RtmpFileShareDurationSec = session.DurationSec;

            await Clients.Group(roomId).rtmpFileShareStarted(
                Context.ConnectionId, dbUser.username, roomId, streamUrl, session.DurationSec);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RtmpFileShare] StartRtmpFileShareFromUpload failed: {ex.Message}");
            room.IsRtmpFileShareActive  = false;
            room.RtmpFileShareInitiator = null;
            room.RtmpFileShareUploadId  = null;
            room.RtmpFileShareAssetKey  = null;
            room.RtmpFileShareUsesVodPlayback = false;
            room.RtmpFileShareDurationSec = null;
            room.RtmpStreamUrl          = null;
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header   = "Error",
                Question = "Failed to start the RTMP file stream. Please try again.",
                Answer1  = "Ok"
            });
        }
    }

    /// <summary>Stops the active RTMP file-share and notifies all room members.</summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StopRtmpFileShare(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || !room.IsRtmpFileShareActive) return;
        if (room.RtmpFileShareInitiator != Context.ConnectionId) return;

        await StopRtmpFileShareInternalAsync(room);
    }

    /// <summary>Toggles play/pause of the active RTMP stream and broadcasts the state to viewers.</summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task RtmpFileSharePlayPause(string token, string roomId, bool isPlaying)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || !room.IsRtmpFileShareActive) return;
        if (room.RtmpFileShareInitiator != Context.ConnectionId) return;

        var session = _rtmpFileShareManager.Get(roomId);
        if (session == null) return;

        await _rtmpFileShareManager.PausePlayAsync(roomId, isPlaying);

        var pos = _rtmpFileShareManager.Get(roomId)?.CurrentPositionSec ?? 0;
        var viewers = Clients.GroupExcept(roomId, new[] { Context.ConnectionId });
        await viewers.rtmpFileSharePlayPause(isPlaying, pos);
    }

    /// <summary>
    /// Seeks the RTMP stream to the given position by killing and restarting ffmpeg.
    /// Blocked while the background upload is still in progress.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task RtmpFileShareSeek(string token, string roomId, double positionSec)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || !room.IsRtmpFileShareActive) return;
        if (room.RtmpFileShareInitiator != Context.ConnectionId) return;

        if (!string.IsNullOrWhiteSpace(room.RtmpFileShareUploadId))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Warning)
            {
                Header   = "Upload In Progress",
                Question = "The file is still uploading. Seeking will be available once the full upload completes.",
                Answer1  = "Ok"
            });
            return;
        }

        await _rtmpFileShareManager.SeekAsync(roomId, positionSec);
        await Clients.Group(roomId).rtmpFileShareSeek(positionSec);
    }

    /// <summary>
    /// Called by the host after the background chunk upload finishes.
    /// Clears the upload guard so seeking becomes available.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public Task MarkRtmpFileShareUploadComplete(string token, string roomId, string uploadId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return Task.CompletedTask;
        if (room.RtmpFileShareInitiator != Context.ConnectionId) return Task.CompletedTask;

        if (room.RtmpFileShareUploadId == uploadId)
            room.RtmpFileShareUploadId = null;

        _rtmpFileShareManager.MarkUploadComplete(roomId, uploadId);
        return Task.CompletedTask;
    }

    // ── Shared internal teardown ───────────────────────────────────────────────

    internal async Task StopRtmpFileShareInternalAsync(Room room)
    {
        var session = _rtmpFileShareManager.Get(room.uniqueId);
        _rtmpFileShareManager.Stop(room.uniqueId);

        if (!string.IsNullOrWhiteSpace(room.RtmpFileShareAssetKey))
            _roomStreamService.CleanupRoomUploadArtifacts(room.RtmpFileShareAssetKey);

        if (session != null)
        {
            var liveUser = _manager.LiveUsers.FirstOrDefault(x => x.id == session.StreamToken);
            if (liveUser != null)
            {
                _manager.LiveUsers.TryTake(out liveUser);
                var liveUsers = _manager.LiveUsers;
                await Clients.Group(General.LoggedInGroupName)
                    .getliveusers(liveUsers.Select(x => x.ToDTO()).ToList());
                await Clients.Group(General.BottedInGroupName)
                    .getliveusers(liveUsers.Select(x => x.ToDTO()).ToList());
            }
        }

        room.IsRtmpFileShareActive  = false;
        room.RtmpFileShareInitiator = null;
        room.RtmpFileShareUploadId  = null;
        room.RtmpFileShareAssetKey  = null;
        room.RtmpFileShareUsesVodPlayback = false;
        room.RtmpFileShareDurationSec = null;
        room.RtmpStreamUrl          = null;

        await Clients.Group(room.uniqueId).rtmpFileShareStopped();
    }
}
