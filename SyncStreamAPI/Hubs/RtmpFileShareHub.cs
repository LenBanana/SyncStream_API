using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Auth;
using SyncStreamAPI.Helper.Streaming;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    // Set in the ServerHub constructor.
    private RtmpFileShareManager _rtmpFileShareManager;

    // ---------------------------------------------------------------
    // RTMP file share: the server transcodes the sharer's upload and pushes it to the streaming server, and the
    // room plays it from there. It holds the room's stage like a broadcast or an SFU file share does.
    // ---------------------------------------------------------------

    /// <summary>
    ///     Starts streaming the caller's upload to the room, as soon as its beginning has arrived. The rest keeps
    ///     uploading; playback waits whenever it catches up.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task StartRtmpFileShareFromUpload(string roomId, string uploadId,
        RtmpPlaybackPreferences? playbackPreferences = null)
    {
        var room = RequireMember(roomId);
        var member = room.server.FindByConnection(Context.ConnectionId)!;

        // Answers 404 for a malformed id, an unknown one, or someone else's, so all three read the same.
        var upload = await _roomStreamService.GetRoomUploadStatusAsync(CallerUserId, uploadId);
        if (upload.StatusCode != StatusCodes.Status200OK || upload.NextOffset <= 0)
            throw new HubException("The upload session could not be found. Please try again.");

        lock (room.StageGate)
        {
            var refusal = StageRefusal(room, Context.ConnectionId)
                          ?? (room.CurrentStreamer != null ? "Stop your broadcast before sharing a file." : null);
            if (refusal != null) throw new HubException(refusal);
            room.IsRtmpFileShareActive = true;
            room.RtmpFileShareInitiator = Context.ConnectionId;
        }

        RtmpFileShareSession session;
        try
        {
            session = await _rtmpFileShareManager.PrepareAsync(roomId, RoomUploadPaths.DataFileFor(uploadId), uploadId,
                upload.TotalSize, upload.NextOffset >= upload.TotalSize, playbackPreferences);
        }
        catch (RtmpFileShareException ex)
        {
            ReleaseRtmpStageClaim(room);
            throw new HubException(ex.Message);
        }
        catch
        {
            ReleaseRtmpStageClaim(room);
            throw;
        }

        // Announced before the first run starts: its "stream ready" must reach players that already know the share.
        await Clients.Group(roomId).rtmpFileShareStarted(Context.ConnectionId, member.username ?? string.Empty,
            roomId, session.StreamName, session.DurationSec);
        await Clients.Group(roomId).rtmpFileShareMetadata(session.DurationSec, session.UploadComplete);
        _rtmpFileShareManager.Begin(session);
    }

    private static void ReleaseRtmpStageClaim(Room room)
    {
        lock (room.StageGate)
        {
            room.IsRtmpFileShareActive = false;
            room.RtmpFileShareInitiator = null;
        }
    }

    /// <summary>Ends the caller's RTMP file share; everyone's player goes back to what it was showing.</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task StopRtmpFileShare(string roomId)
    {
        var room = RequireMember(roomId);
        if (!IsRtmpSharer(room)) return;

        await _media.StopRtmpFileShareAsync(room);
    }

    /// <summary>Pauses or resumes the caller's share for everyone.</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task RtmpFileSharePlayPause(string roomId, bool isPlaying)
    {
        var room = RequireMember(roomId);
        if (!IsRtmpSharer(room)) return;

        var changed = isPlaying ? _rtmpFileShareManager.Resume(roomId) : _rtmpFileShareManager.Pause(roomId);
        var session = _rtmpFileShareManager.Get(roomId);
        if (!changed || session == null) return;

        await Clients.Group(roomId).rtmpFileSharePlayPause(isPlaying, session.Status().PositionSec);
    }

    /// <summary>Moves the caller's share to a position, once the whole file has been uploaded.</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task RtmpFileShareSeek(string roomId, double positionSec)
    {
        var room = RequireMember(roomId);
        if (!IsRtmpSharer(room)) return;

        var session = _rtmpFileShareManager.Get(roomId);
        if (session == null) return;
        if (!double.IsFinite(positionSec))
            throw new HubException("That is not a position in the file.");

        // A position past what has been uploaded cannot be played, so seeking waits for the whole file.
        if (!session.Status().UploadComplete)
            throw new HubException("The file is still uploading. Seeking will be available once the upload completes.");

        if (!_rtmpFileShareManager.Seek(roomId, positionSec)) return;
        await Clients.Group(roomId).rtmpFileShareSeek(session.Status().PositionSec);
    }

    /// <summary>
    ///     The sharer's upload has finished. From now on the end of the file is the end of the share, and seeking
    ///     becomes possible.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task MarkRtmpFileShareUploadComplete(string roomId, string uploadId)
    {
        var room = RequireMember(roomId);
        if (!IsRtmpSharer(room)) return;

        var session = _rtmpFileShareManager.Get(roomId);
        if (session == null || session.UploadId != uploadId) return;

        var upload = await _roomStreamService.GetRoomUploadStatusAsync(CallerUserId, uploadId);
        if (upload.StatusCode != StatusCodes.Status200OK || upload.NextOffset != upload.TotalSize)
            throw new HubException("The upload is not complete yet.");

        await _rtmpFileShareManager.MarkUploadCompleteAsync(roomId);
        await Clients.Group(roomId).rtmpFileShareMetadata(session.DurationSec, true);
    }

    private bool IsRtmpSharer(Room room)
        => room.IsRtmpFileShareActive && room.RtmpFileShareInitiator == Context.ConnectionId;
}
