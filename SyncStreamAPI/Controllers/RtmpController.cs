using System;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Helper.Streaming;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/rtmp")]
public class RtmpController : Controller
{
    private static readonly HttpClient LiveProxyClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        UseProxy = false,
    })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    private readonly IConfiguration _configuration;
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly MainManager _manager;
    private readonly PostgresContext _postgres;
    private readonly RtmpFileShareManager _rtmpFileShareManager;

    public RtmpController(
        IConfiguration configuration,
        IHubContext<ServerHub, IServerHub> hub,
        PostgresContext postgres,
        MainManager manager,
        RtmpFileShareManager rtmpFileShareManager)
    {
        _configuration = configuration;
        _hub = hub;
        _postgres = postgres;
        _manager = manager;
        _rtmpFileShareManager = rtmpFileShareManager;
    }

    [HttpGet("[action]")]
    public async Task<IActionResult> liveProxy([FromQuery] string stream, [FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(stream) || string.IsNullOrWhiteSpace(token))
            return BadRequest("Missing required stream parameters");

        try
        {
            var liveBase = _configuration["LiveStreamBaseUrl"]?.TrimEnd('/') ?? "https://live.drecktu.be/live";
            var targetUrl = liveBase + Request.QueryString;

            using var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            using var upstreamResponse = await LiveProxyClient.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                HttpContext.RequestAborted);

            if (!upstreamResponse.IsSuccessStatusCode)
                return StatusCode((int)upstreamResponse.StatusCode);

            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            Response.ContentType = upstreamResponse.Content.Headers.ContentType?.ToString() ?? "video/x-flv";
            Response.Headers[HeaderNames.CacheControl] = "no-store, no-cache, must-revalidate";
            Response.Headers[HeaderNames.Pragma] = "no-cache";
            Response.Headers[HeaderNames.Expires] = "0";

            await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
            await upstreamStream.CopyToAsync(Response.Body, HttpContext.RequestAborted);
            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RTMP liveProxy failed: {ex}");
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> onpublish([FromForm] RtmpData rtmpData)
    {
        try
        {
            Console.WriteLine($"onpublish: {rtmpData.name}");
            var dbUser = _postgres.Users?.FirstOrDefault(x =>
                x.StreamToken != null && x.StreamToken == rtmpData.token &&
                x.username.ToLower() == rtmpData.name.ToLower());
            var token = dbUser?.StreamToken;
            if (string.IsNullOrEmpty(token) || dbUser.userprivileges < UserPrivileges.Approved) return Unauthorized();

            var liveUser = _manager.LiveUsers.FirstOrDefault(x => x.id == rtmpData.token);
            if (liveUser != null) return Ok();
            {
                _manager.LiveUsers.Add(new LiveUser { userName = dbUser.username, id = rtmpData.token });
                var liveUsers = _manager.LiveUsers;
                await _hub.Clients.Groups(General.LoggedInGroupName)
                    .getliveusers(liveUsers.Select(x => x.ToDTO()).ToList());
                await _hub.Clients.Groups(General.BottedInGroupName)
                    .getliveusers(liveUsers.Select(x => x.ToDTO()).ToList());
            }

            if (_rtmpFileShareManager.TryMarkPublisherLive(token, rtmpData.name, out var roomId, out var positionSec))
                await _hub.Clients.Group(roomId).rtmpFileShareStreamReady(positionSec);

            return Ok();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> onpublishdone([FromForm] RtmpData rtmpData)
    {
        try
        {
            Console.WriteLine($"onpublishdone: {rtmpData.name}");
            var dbUser = _postgres.Users?.FirstOrDefault(x => x.StreamToken != null && x.StreamToken == rtmpData.token);
            var token = dbUser?.StreamToken;
            if (string.IsNullOrEmpty(token) || dbUser.userprivileges < UserPrivileges.Approved) return Unauthorized();

            if (_rtmpFileShareManager.TryConsumeExpectedPublishDone(token, rtmpData.name))
            {
                Console.WriteLine($"[RtmpShare/{rtmpData.name}] suppressing transient onpublishdone during managed pause/seek");
                return Ok();
            }

            var liveUser = _manager.LiveUsers.FirstOrDefault(x => x.id == rtmpData.token);
            if (liveUser == null) return Ok();
            {
                _manager.LiveUsers.TryTake(out liveUser);
                var liveUsers = _manager.LiveUsers;
                await _hub.Clients.Groups(General.LoggedInGroupName)
                    .getliveusers(liveUsers.Select(x => x.ToDTO()).ToList());
                await _hub.Clients.Groups(General.BottedInGroupName)
                    .getliveusers(liveUsers.Select(x => x.ToDTO()).ToList());
            }
            return Ok();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> onplay([FromForm] RtmpData rtmpData)
    {
        try
        {
            Console.WriteLine($"onplay: {rtmpData.name}");
            var liveUser = _manager.LiveUsers?.FirstOrDefault(x =>
                string.Equals(x.userName, rtmpData.name, StringComparison.CurrentCultureIgnoreCase));
            if (liveUser == null) return StatusCode(StatusCodes.Status404NotFound);

            var dbUser = _postgres.Users?.Include(x => x.RememberTokens)?.FirstOrDefault(x =>
                x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == rtmpData.token));
            if (dbUser == null || dbUser.userprivileges < UserPrivileges.Approved) return Unauthorized();

            liveUser.watchMember.Add(dbUser.ToDTO());
            var liveUsers = _manager.LiveUsers;
            await _hub.Clients.Groups(General.LoggedInGroupName)
                .getliveusers(liveUsers.Select(x => x.ToDTO()).ToList());
            return Ok();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> onplaydone([FromForm] RtmpData rtmpData)
    {
        try
        {
            Console.WriteLine($"onplaydone: {rtmpData.name}");
            var liveUser = _manager.LiveUsers?.FirstOrDefault(x =>
                string.Equals(x.userName, rtmpData.name, StringComparison.CurrentCultureIgnoreCase));
            if (liveUser == null) return StatusCode(StatusCodes.Status404NotFound);

            var dbUser = _postgres.Users?.Include(x => x.RememberTokens)?.FirstOrDefault(x =>
                x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == rtmpData.token));
            if (dbUser == null || dbUser.userprivileges < UserPrivileges.Approved) return Unauthorized();

            var watchMemberIdx = liveUser.watchMember.FindIndex(x => x.ID == dbUser.ID);
            if (watchMemberIdx != -1) liveUser.watchMember.RemoveAt(watchMemberIdx);

            var liveUsers = _manager.LiveUsers;
            await _hub.Clients.Groups(General.LoggedInGroupName)
                .getliveusers(liveUsers.Select(x => x.ToDTO()).ToList());
            return Ok();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}