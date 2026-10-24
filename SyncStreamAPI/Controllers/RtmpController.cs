using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Helper.Streaming;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.Wire;

namespace SyncStreamAPI.Controllers;

/// <summary>
///     The streaming server's callbacks. nginx-rtmp asks here before letting anyone publish or play, and reports
///     when they stop.
/// </summary>
/// <remarks>
///     These answer anyone who calls them, so each one checks a credential the publisher or player put in its
///     URL and nginx passed on: an account's stream token for OBS, a share's own key for the server's ffmpeg, and
///     a play ticket from GetLivePlayUrl for players.
/// </remarks>
[EnableCors("CORSPolicy")]
[Route("api/rtmp")]
public class RtmpController : Controller
{
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly MainManager _manager;
    private readonly PostgresContext _postgres;
    private readonly RtmpFileShareManager _rtmpFileShareManager;
    private readonly LivePlayTickets _tickets;

    public RtmpController(
        IHubContext<ServerHub, IServerHub> hub,
        PostgresContext postgres,
        MainManager manager,
        RtmpFileShareManager rtmpFileShareManager,
        LivePlayTickets tickets)
    {
        _hub = hub;
        _postgres = postgres;
        _manager = manager;
        _rtmpFileShareManager = rtmpFileShareManager;
        _tickets = tickets;
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> onpublish([FromForm] RtmpData rtmpData)
    {
        if (string.IsNullOrWhiteSpace(rtmpData.name) || string.IsNullOrWhiteSpace(rtmpData.token))
            return Refuse("publish", rtmpData.name, "no stream name or key");

        if (RtmpFileShareManager.IsShareStream(rtmpData.name))
        {
            var share = _rtmpFileShareManager.AcceptPublisher(rtmpData.name, rtmpData.token);
            if (share == null) return Refuse("publish", rtmpData.name, "not a running file share, or the wrong key");

            Log("publish", rtmpData.name, $"accepted, file share in room {share.RoomId}");
            // Every run is a new stream with timestamps starting from zero; players connect afresh.
            await _hub.Clients.Group(share.RoomId).rtmpFileShareStreamReady(share.Status().PositionSec);
            return Ok();
        }

        var name = rtmpData.name.ToLower();
        var dbUser = await _postgres.Users.FirstOrDefaultAsync(user =>
            user.StreamToken == rtmpData.token && user.username.ToLower() == name);
        if (dbUser == null) return Refuse("publish", rtmpData.name, "no account with that name and stream key");
        if (dbUser.userprivileges < UserPrivileges.Approved) return Refuse("publish", rtmpData.name, "account not approved");

        var added = _manager.LiveUsers.TryAdd(dbUser.StreamToken,
            new LiveUser { id = dbUser.StreamToken, userName = dbUser.username, StreamName = rtmpData.name });
        Log("publish", rtmpData.name, added ? "accepted" : "accepted, already listed live");
        if (added) await SendLiveUsersAsync(toBots: true);
        return Ok();
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> onpublishdone([FromForm] RtmpData rtmpData)
    {
        Log("publish done", rtmpData.name, "stream ended");
        if (RtmpFileShareManager.IsShareStream(rtmpData.name))
        {
            _rtmpFileShareManager.PublisherGone(rtmpData.name, rtmpData.token);
            return Ok();
        }

        if (string.IsNullOrEmpty(rtmpData.token) || !_manager.LiveUsers.TryRemove(rtmpData.token, out _))
            return Ok();

        await SendLiveUsersAsync(toBots: true);
        return Ok();
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> onplay([FromForm] RtmpData rtmpData)
    {
        if (!_tickets.TryRead(rtmpData.token, out var ticket) || ticket.Stream != rtmpData.name)
            return Refuse("play", rtmpData.name, "no ticket for this stream");
        if (ticket.ExpiresAt < DateTimeOffset.UtcNow)
            return Refuse("play", rtmpData.name, "ticket expired");

        Log("play", rtmpData.name, ticket.UserId is { } account ? $"accepted for account {account}" : "accepted for a guest");
        var liveUser = _manager.FindLiveUser(rtmpData.name);
        if (liveUser == null || ticket.UserId is not { } viewerId) return Ok();

        var viewer = await _postgres.Users.FirstOrDefaultAsync(user => user.ID == viewerId);
        if (viewer == null) return Ok();

        lock (liveUser.watchMember)
            liveUser.watchMember.Add(viewer.ToSummary());
        await SendLiveUsersAsync(toBots: false);
        return Ok();
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> onplaydone([FromForm] RtmpData rtmpData)
    {
        // The ticket a play started with, which has usually expired by the time the play ends.
        if (!_tickets.TryRead(rtmpData.token, out var ticket) || ticket.Stream != rtmpData.name)
            return Unauthorized();

        var liveUser = _manager.FindLiveUser(rtmpData.name);
        if (liveUser == null || ticket.UserId is not { } viewerId) return Ok();

        bool removed;
        lock (liveUser.watchMember)
        {
            var index = liveUser.watchMember.FindIndex(member => member.ID == viewerId);
            removed = index >= 0;
            if (removed) liveUser.watchMember.RemoveAt(index);
        }

        if (removed) await SendLiveUsersAsync(toBots: false);
        return Ok();
    }

    /// <summary>
    ///     One line per decision, so the server log shows whether the streaming server reached the API and what it
    ///     was told. Keys and tickets stay out of it.
    /// </summary>
    private static void Log(string callback, string? stream, string outcome)
        => Console.WriteLine($"[RTMP] {callback} {stream ?? "(no stream)"}: {outcome}");

    private UnauthorizedResult Refuse(string callback, string? stream, string reason)
    {
        Log(callback, stream, $"refused, {reason}");
        return Unauthorized();
    }

    private async Task SendLiveUsersAsync(bool toBots)
    {
        var liveUsers = _manager.LiveUsersInOrder().ToDtos();
        await _hub.Clients.Group(General.LoggedInGroupName).getliveusers(liveUsers);
        if (toBots) await _hub.Clients.Group(General.BottedInGroupName).getliveusers(liveUsers);
    }
}
