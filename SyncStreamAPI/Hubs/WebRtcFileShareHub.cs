using System.Linq;
using System.Threading.Tasks;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
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
    /// Called by the file-share initiator to broadcast the current play/pause state
    /// to all other room members so they can synchronise their viewer.
    /// </summary>
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task FileSharePlayPause(string token, string roomId, bool isPlaying)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null || !room.IsFileSharingActive) return;
        if (room.FileShareInitiator != Context.ConnectionId) return;

        await Clients.GroupExcept(roomId, new[] { Context.ConnectionId })
            .fileSharePlayPause(isPlaying);
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
