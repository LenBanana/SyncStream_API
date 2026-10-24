using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Auth;
using SyncStreamAPI.Helper.Streaming;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Helper;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    // ---------------------------------------------------------------
    // File Share: anyone with Approved privileges may share a file to the room's stage.
    // Only one share or broadcast may hold the stage at a time.
    // ---------------------------------------------------------------

    /// <summary>
    /// Takes the stage for a file played in the caller's browser. Everyone's player switches to the file share
    /// and joins the stage's SFU room; the caller then produces the captured file into it.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task StartFileShare(string roomId)
    {
        var room = RequireMember(roomId);
        var member = room.server.FindByConnection(Context.ConnectionId)!;

        lock (room.StageGate)
        {
            if (StageRefusal(room, Context.ConnectionId) is { } refusal)
                throw new HubException(refusal);
            ClaimStageForFileShare(room, Context.ConnectionId, uploadId: null);
        }

        await RoomManager.SendPlayerType(room);
        await Clients.Group(roomId).fileShareStarted(Context.ConnectionId, member.username ?? string.Empty, roomId);
    }

    /// <summary>
    /// Starts sharing a file while it is still being uploaded: the server streams what has arrived and waits
    /// whenever playback catches up with the upload. The upload is the caller's own and must already hold the
    /// beginning of the file.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task StartServerFileShareFromUpload(string roomId, string uploadId)
    {
        var room = RequireMember(roomId);

        // Answers 404 for a malformed id, an unknown one, or someone else's, so all three read the same.
        var upload = await _roomStreamService.GetRoomUploadStatusAsync(CallerUserId, uploadId);
        if (upload.StatusCode != StatusCodes.Status200OK || upload.NextOffset <= 0)
            throw new HubException("The upload session could not be found. Please try again.");

        await StartServerStreamOnStageAsync(room, RoomUploadPaths.DataFileFor(uploadId), uploadId);
    }

    private async Task StartServerStreamOnStageAsync(Room room, string filePath, string? uploadId)
    {
        var member = room.server.FindByConnection(Context.ConnectionId)!;
        var stage = SfuRoomId.Stage(room.uniqueId);

        lock (room.StageGate)
        {
            if (StageRefusal(room, Context.ConnectionId) is { } refusal)
                throw new HubException(refusal);
            ClaimStageForFileShare(room, Context.ConnectionId, uploadId);
        }

        try
        {
            await SfuManager.EnsureRoomAsync(stage);
            await SfuManager.StartServerFileStreamAsync(stage, filePath, growing: uploadId != null);
        }
        catch (SfuRequestException ex)
        {
            lock (room.StageGate)
            {
                room.IsFileSharingActive = false;
                room.FileShareInitiator = null;
                room.FileShareUploadId = null;
                room.FileShareUploadInProgress = false;
                room.CurrentStreamer = null;
                room.IsStreamingSfu = false;
            }

            Console.WriteLine($"[FileShare] server stream for room {room.uniqueId} failed to start: {ex.Message}");
            throw new HubException(ex.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
                ? ex.Message
                : $"The server could not stream this file: {ex.Message}");
        }

        // Set only once the stream exists, because the monitor ends a server share it cannot find.
        room.IsServerFileShare = true;

        await RoomManager.SendPlayerType(room);
        await Clients.Group(room.uniqueId)
            .fileShareStarted(Context.ConnectionId, member.username ?? string.Empty, room.uniqueId);
    }

    private static void ClaimStageForFileShare(Room room, string connectionId, string? uploadId)
    {
        room.IsFileSharingActive = true;
        room.IsFileSharePaused = false;
        room.IsServerFileShare = false;
        room.FileShareInitiator = connectionId;
        room.FileShareUploadId = uploadId;
        room.FileShareUploadInProgress = uploadId != null;
        room.CurrentStreamer = connectionId;
        room.IsStreamingSfu = true;
    }

    /// <summary>Ends the caller's file share; everyone's player goes back to what it was showing.</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task StopFileShare(string roomId)
    {
        var room = RequireMember(roomId);
        if (!room.IsFileSharingActive || room.FileShareInitiator != Context.ConnectionId) return;

        await _media.StopFileShareAsync(room);
        await _media.ReleaseSfuAsync(Context.ConnectionId, candidate => candidate == SfuRoomId.Stage(roomId));
    }

    /// <summary>
    /// Called by the file-share initiator to broadcast the current play/pause state.
    /// For server-side shares, also controls the ffmpeg process directly.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task FileSharePlayPause(string roomId, bool isPlaying)
    {
        var room = RequireMember(roomId);
        if (!room.IsFileSharingActive || room.FileShareInitiator != Context.ConnectionId) return;

        if (room.IsServerFileShare)
            await SfuManager.ControlServerFileStreamAsync(SfuRoomId.Stage(roomId), isPlaying ? "play" : "pause");

        room.IsFileSharePaused = !isPlaying;
        await Clients.GroupExcept(roomId, new[] { Context.ConnectionId }).fileSharePlayPause(isPlaying);
    }

    /// <summary>
    /// Seeks the server-side file share to the given position.
    /// No-op for browser-captureStream shares (seek is handled client-side there).
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task FileShareSeek(string roomId, double positionSec)
    {
        var room = RequireMember(roomId);
        if (!room.IsFileSharingActive || room.FileShareInitiator != Context.ConnectionId) return;
        if (!room.IsServerFileShare) return;

        // While the upload is still arriving, a position past what has been uploaded cannot be played. Seeking
        // waits for the whole file.
        if (room.FileShareUploadInProgress)
            throw new HubException("The file is still uploading. Seeking will be available once the upload completes.");

        await SfuManager.ControlServerFileStreamAsync(SfuRoomId.Stage(roomId), "seek", positionSec);
        room.IsFileSharePaused = false;
        await Clients.GroupExcept(roomId, new[] { Context.ConnectionId }).fileSharePlayPause(true);
    }

    /// <summary>
    /// The sharer's upload has finished. From now on the end of the file is the end of the share, and seeking
    /// becomes possible.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task MarkFileShareUploadComplete(string roomId, string uploadId)
    {
        var room = RequireMember(roomId);
        if (room.FileShareInitiator != Context.ConnectionId || room.FileShareUploadId != uploadId) return;

        var upload = await _roomStreamService.GetRoomUploadStatusAsync(CallerUserId, uploadId);
        if (upload.StatusCode != StatusCodes.Status200OK || upload.NextOffset != upload.TotalSize)
            throw new HubException("The upload is not complete yet.");

        await SfuManager.ControlServerFileStreamAsync(SfuRoomId.Stage(roomId), "upload-complete");
        room.FileShareUploadInProgress = false;
    }
}
