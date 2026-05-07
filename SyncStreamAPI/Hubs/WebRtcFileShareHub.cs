using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Helper;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    // ---------------------------------------------------------------
    // File Share — anyone with Approved privileges may share a file.
    // Only one file share may be active per room at a time.
    // ---------------------------------------------------------------

    /// <summary>
    /// Called by the initiating client just after their SFU send-transport produces
    /// the first track.  Sets the room's file-sharing flag and announces the share
    /// to all room members so they switch their player to the FileShare type and
    /// join the SFU room as viewers.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StartFileShare(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;

        if (room.IsFileSharingActive)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Warning)
            {
                Header = "File Share Unavailable",
                Question = "Someone is already sharing a file in this room. Please wait until they finish.",
                Answer1 = "Ok"
            });
            return;
        }

        var member = room.server.members.FirstOrDefault(m => m?.ConnectionId == Context.ConnectionId);
        if (member == null) return;

        room.IsFileSharingActive = true;
        room.FileShareInitiator = Context.ConnectionId;
        room.CurrentStreamer = Context.ConnectionId;
        room.IsStreamingSfu = true;

        // Send FileShare player type to all room members (SendPlayerType checks IsFileSharingActive first).
        await RoomManager.SendPlayerType(room);

        // Notify the room so viewers know who is sharing and can join the SFU room.
        await Clients.Group(roomId).fileShareStarted(Context.ConnectionId, member.username, roomId);
    }

    /// <summary>
    /// Called when the file's codecs are not natively playable in a browser.
    /// The server transcodes via ffmpeg and injects audio+video as mediasoup
    /// producers; all room members — including the host — consume via the SFU.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StartServerFileShare(string token, string roomId, string fileKey)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;

        if (room.IsFileSharingActive)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Warning)
            {
                Header = "File Share Unavailable",
                Question = "Someone is already sharing a file in this room. Please wait until they finish.",
                Answer1 = "Ok"
            });
            return;
        }

        var member = room.server.members.FirstOrDefault(m => m?.ConnectionId == Context.ConnectionId);
        if (member == null) return;

        // Resolve the caller and verify they own the file (or it's public).
        // Without this check any Approved user could share another user's file by guessing the key.
        var dbUser = _postgres.Users?
            .Include(x => x.RememberTokens)
            .FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == token));
        if (dbUser == null) return;

        var dbFile = _postgres.Files?.FirstOrDefault(x => x.FileKey == fileKey);
        if (dbFile == null)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "File Not Found", Question = "The selected file could not be found.", Answer1 = "Ok" });
            return;
        }

        if (!dbFile.Public && dbFile.DbUserID != dbUser.ID)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Permission Denied", Question = "You do not have permission to share this file.", Answer1 = "Ok" });
            return;
        }

        var basePath = dbFile.Temporary ? General.TemporaryFilePath : General.FilePath;
        var filePath = Path.Combine(basePath, $"{dbFile.FileKey}{dbFile.FileEnding}");

        if (!File.Exists(filePath))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "File Not Found", Question = "The file could not be found on disk.", Answer1 = "Ok" });
            return;
        }

        room.IsFileSharingActive = true;
        room.FileShareInitiator = Context.ConnectionId;
        room.CurrentStreamer = Context.ConnectionId;
        room.IsStreamingSfu = true;
        room.IsServerFileShare = true;

        await SfuManager.EnsureRoomAsync(roomId);

        try
        {
            var (videoProducerId, audioProducerId) = await SfuManager.StartServerFileStreamAsync(roomId, filePath);

            // Notify peers already in the SFU room (e.g. previous viewers) about the new producers.
            var serverPeerId = $"server-file:{roomId}";
            await Clients.Group(SfuGroupName(roomId))
                .sfuNewProducer(new SfuNewProducerNotification
                    { ProducerId = videoProducerId, PeerId = serverPeerId, Kind = "video", RoomId = roomId });
            await Clients.Group(SfuGroupName(roomId))
                .sfuNewProducer(new SfuNewProducerNotification
                    { ProducerId = audioProducerId, PeerId = serverPeerId, Kind = "audio", RoomId = roomId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileShare] StartServerFileShare failed: {ex.Message}");
            room.IsFileSharingActive = false;
            room.FileShareInitiator = null;
            room.CurrentStreamer = null;
            room.IsStreamingSfu = false;
            room.IsServerFileShare = false;
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Failed to start the server-side file stream.", Answer1 = "Ok" });
            return;
        }

        await RoomManager.SendPlayerType(room);
        await Clients.Group(roomId).fileShareStarted(Context.ConnectionId, member.username, roomId);
    }

    /// <summary>
    /// Variant of <see cref="StartServerFileShare"/> for the early-stream path: the client
    /// has uploaded the first chunk and wants to start ffmpeg immediately, continuing to
    /// upload the remaining chunks in the background while viewers already watch.
    /// The upload temp file at <c>{uploads}/{uploadId}/data</c> is used directly by ffmpeg;
    /// no DB record is created until <c>CompleteRoomUpload</c> is later called.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StartServerFileShareFromUpload(string token, string roomId, string uploadId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null) return;

        if (room.IsFileSharingActive)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Warning)
            {
                Header = "File Share Unavailable",
                Question = "Someone is already sharing a file in this room. Please wait until they finish.",
                Answer1 = "Ok"
            });
            return;
        }

        var member = room.server.members.FirstOrDefault(m => m?.ConnectionId == Context.ConnectionId);
        if (member == null) return;

        // uploadId is a GUID (32 hex chars) — cryptographically unguessable; no separate
        // ownership query needed.  File must already exist (first chunk was written).
        var filePath = Path.Combine(General.TemporaryFilePath, "uploads", uploadId, "data");
        if (!File.Exists(filePath))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Upload Not Found", Question = "The upload session could not be found. Please try again.", Answer1 = "Ok" });
            return;
        }

        room.IsFileSharingActive = true;
        room.FileShareInitiator  = Context.ConnectionId;
        room.CurrentStreamer      = Context.ConnectionId;
        room.IsStreamingSfu      = true;
        room.IsServerFileShare   = true;
        room.FileShareUploadId   = uploadId;

        await SfuManager.EnsureRoomAsync(roomId);

        try
        {
            var (videoProducerId, audioProducerId) = await SfuManager.StartServerFileStreamAsync(roomId, filePath);

            var serverPeerId = $"server-file:{roomId}";
            await Clients.Group(SfuGroupName(roomId))
                .sfuNewProducer(new SfuNewProducerNotification
                    { ProducerId = videoProducerId, PeerId = serverPeerId, Kind = "video", RoomId = roomId });
            await Clients.Group(SfuGroupName(roomId))
                .sfuNewProducer(new SfuNewProducerNotification
                    { ProducerId = audioProducerId, PeerId = serverPeerId, Kind = "audio", RoomId = roomId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileShare] StartServerFileShareFromUpload failed: {ex.Message}");
            room.IsFileSharingActive = false;
            room.FileShareInitiator  = null;
            room.CurrentStreamer     = null;
            room.IsStreamingSfu     = false;
            room.IsServerFileShare  = false;
            room.FileShareUploadId  = null;
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Failed to start the server-side file stream.", Answer1 = "Ok" });
            return;
        }

        await RoomManager.SendPlayerType(room);
        await Clients.Group(roomId).fileShareStarted(Context.ConnectionId, member.username, roomId);
    }

    /// <summary>
    /// Called by the file-share initiator to cleanly stop the active share.
    /// The initiator should call <c>stopStreamingSfu</c> on the client side after
    /// this returns to tear down their SFU send-transport and producers.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task StopFileShare(string token, string roomId)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || !room.IsFileSharingActive) return;
        if (room.FileShareInitiator != Context.ConnectionId) return;

        await StopFileShareInternalAsync(room);
    }

    /// <summary>
    /// Called by the file-share initiator to broadcast the current play/pause state.
    /// For server-side shares, also controls the ffmpeg process directly.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task FileSharePlayPause(string token, string roomId, bool isPlaying)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || !room.IsFileSharingActive) return;
        if (room.FileShareInitiator != Context.ConnectionId) return;

        if (room.IsServerFileShare)
        {
            try { await SfuManager.ControlServerFileStreamAsync(roomId, isPlaying ? "play" : "pause"); }
            catch (Exception ex) { Console.WriteLine($"[FileShare] PlayPause control failed: {ex.Message}"); }
        }

        await Clients.GroupExcept(roomId, new[] { Context.ConnectionId })
            .fileSharePlayPause(isPlaying);
    }

    /// <summary>
    /// Seeks the server-side file share to the given position.
    /// No-op for browser-captureStream shares (seek is handled client-side there).
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task FileShareSeek(string token, string roomId, double positionSec)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || !room.IsFileSharingActive) return;
        if (room.FileShareInitiator != Context.ConnectionId) return;
        if (!room.IsServerFileShare) return;

        try { await SfuManager.ControlServerFileStreamAsync(roomId, "seek", positionSec); }
        catch (Exception ex) { Console.WriteLine($"[FileShare] Seek failed: {ex.Message}"); }
    }

    // ---------------------------------------------------------------
    // Shared internal teardown — also called from OnDisconnectedAsync
    // and NextVideo so the room state is always consistent.
    // ---------------------------------------------------------------

    /// <summary>
    /// Clears all file-share flags, notifies the room, and re-evaluates the
    /// player type so clients switch back to whatever was playing before.
    /// </summary>
    internal async Task StopFileShareInternalAsync(Room room)
    {
        if (room.IsServerFileShare)
        {
            try { await SfuManager.StopServerFileStreamAsync(room.uniqueId); }
            catch (Exception ex) { Console.WriteLine($"[FileShare] StopServerFileStream failed: {ex.Message}"); }
            room.IsServerFileShare = false;
        }

        // Clean up the upload temp directory left by an early-stream upload.
        if (!string.IsNullOrWhiteSpace(room.FileShareUploadId))
        {
            var uploadDir = Path.Combine(General.TemporaryFilePath, "uploads", room.FileShareUploadId);
            try { if (Directory.Exists(uploadDir)) Directory.Delete(uploadDir, recursive: true); }
            catch (Exception ex) { Console.WriteLine($"[FileShare] Failed to delete upload dir: {ex.Message}"); }
            room.FileShareUploadId = null;
        }

        room.IsFileSharingActive = false;
        room.FileShareInitiator = null;
        room.CurrentStreamer = null;
        room.IsStreamingSfu = false;

        // Notify all room members so they can tear down their viewer state.
        await Clients.Group(room.uniqueId).fileShareStopped();

        // Re-evaluate and broadcast the correct player type (e.g. Nothing, YouTube, …).
        await RoomManager.SendPlayerType(room);
    }
}
