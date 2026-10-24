using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Auth;
using SyncStreamAPI.Helper.Streaming;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.Wire;
using SyncStreamAPI.Wire.Users;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    // Set in the ServerHub constructor.
    private LivePlayTickets _liveTickets;

    private string LiveStreamBaseUrl =>
        Configuration["LiveStreamBaseUrl"]?.TrimEnd('/') ?? "https://live.drecktu.be/live";

    private string RtmpUrl => Configuration["RtmpUrl"]?.TrimEnd('/') ?? "rtmp://rtmp-server:1935/live";

    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task GetLiveUsers()
    {
        if (!_manager.LiveUsers.IsEmpty) await Clients.Caller.getliveusers(_manager.LiveUsersInOrder().ToDtos());
    }

    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task GetUsersWatching(string name)
    {
        var liveUser = _manager.LiveUsers.Values.FirstOrDefault(x => x.userName == name);
        if (liveUser == null) return;
        List<UserSummaryDto> watching;
        lock (liveUser.watchMember)
            watching = liveUser.watchMember.ToList();
        await Clients.Caller.getwatchingusers(watching);
    }

    /// <summary>
    ///     The URL to play a live stream from, with a ticket the streaming server's play check accepts, or null
    ///     when nobody is streaming it. Accounts may watch any OBS stream, as the live list shows them all;
    ///     anyone in a room may watch the room's RTMP file share and the live video the room is showing.
    /// </summary>
    /// <remarks>Open to guests, who are checked here against the room they are in.</remarks>
    public string? GetLivePlayUrl(string? roomId, string streamName)
    {
        var caller = Context.GetCaller();
        string stream;
        bool allowed;

        if (RtmpFileShareManager.IsShareStream(streamName))
        {
            var share = _rtmpFileShareManager.FindByStream(streamName);
            if (share == null) return null;
            stream = share.StreamName;
            allowed = MainManager.GetRoom(share.RoomId)?.server.FindByConnection(Context.ConnectionId) != null;
        }
        else
        {
            var live = _manager.FindLiveUser(streamName);
            if (live == null) return null;
            stream = live.StreamName;
            allowed = caller.HasAtLeast(UserPrivileges.Approved) || RoomShowsLiveStream(roomId, live);
        }

        if (!allowed) throw new HubException("You cannot watch that stream.");

        var ticket = _liveTickets.Issue(stream, caller.UserId, DateTimeOffset.UtcNow + LivePlayTickets.Lifetime);
        return LiveStreamUrls.PlayUrl(LiveStreamBaseUrl, RtmpUrl, stream, ticket);
    }

    private bool RoomShowsLiveStream(string? roomId, LiveUser live)
    {
        var room = MainManager.GetRoom(roomId ?? string.Empty);
        if (room?.server.FindByConnection(Context.ConnectionId) == null) return false;

        var shown = LiveStreamUrls.StreamNameOf(room.server.currentVideo?.url);
        return string.Equals(shown, live.StreamName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(shown, live.userName, StringComparison.OrdinalIgnoreCase);
    }
}
