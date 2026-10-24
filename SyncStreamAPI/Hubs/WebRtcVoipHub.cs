using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.Auth;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.Hubs;

/// <summary>
///     The room's voice and video call. Media runs through the SFU like every other stream: each participant
///     sends once and receives everyone else, instead of uploading a copy to every other participant.
/// </summary>
/// <remarks>
///     The call used to be a peer-to-peer mesh whose offers and ICE candidates the hub relayed to any connection
///     id it was given, and member lists carry every member's connection id. Anyone could send an offer to someone
///     in a call and be answered with that person's microphone, without appearing in their call. Here there is
///     nothing to relay: joining the call needs room membership, and sending into it needs a seat in it.
/// </remarks>
public partial class ServerHub
{
    /// <summary>
    /// Joins the room's call, leaving any other call first. Returns everyone already in it; the caller then
    /// joins the call's SFU room to send and receive.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task<List<VoipParticipantDto>> JoinAudioRoom(string roomId)
    {
        var room = RequireMember(roomId);
        var member = room.server.FindByConnection(Context.ConnectionId)!;

        if (_voip.RoomOf(Context.ConnectionId) is { } current && current != roomId)
            await _media.LeaveVoiceAsync(Context.ConnectionId);

        var (_, others) = _voip.Join(Context.ConnectionId, roomId, member.username ?? string.Empty);
        var group = SfuGroupName(SfuRoomId.Voice(roomId));
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await Clients.GroupExcept(group, new[] { Context.ConnectionId })
            .participantJoined(_voip.Describe(Context.ConnectionId)!);
        return others.ToList();
    }

    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task LeaveAudioRoom(string roomId)
    {
        if (_voip.IsIn(Context.ConnectionId, roomId))
            await _media.LeaveVoiceAsync(Context.ConnectionId);
    }

    /// <summary>Tells the rest of the call whether the caller's microphone, camera and screen are on.</summary>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task SendVoipStatus(string roomId, bool isMuted, bool isVideoMuted, bool isScreenMuted)
    {
        if (!_voip.IsIn(Context.ConnectionId, roomId)) return;

        var participant = _voip.UpdateStatus(Context.ConnectionId, isMuted, isVideoMuted, isScreenMuted);
        if (participant == null) return;
        await Clients.GroupExcept(SfuGroupName(SfuRoomId.Voice(roomId)), new[] { Context.ConnectionId })
            .receiveStatusFromParticipant(participant);
    }
}
