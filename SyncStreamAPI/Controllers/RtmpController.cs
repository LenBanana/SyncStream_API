using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;
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
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly MainManager _manager;
    private readonly PostgresContext _postgres;

    public RtmpController(IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres, MainManager manager)
    {
        _hub = hub;
        _postgres = postgres;
        _manager = manager;
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> onpublish([FromForm] RtmpData rtmpData)
    {
        try
        {
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
            var dbUser = _postgres.Users?.FirstOrDefault(x => x.StreamToken != null && x.StreamToken == rtmpData.token);
            var token = dbUser?.StreamToken;
            if (string.IsNullOrEmpty(token) || dbUser.userprivileges < UserPrivileges.Approved) return Unauthorized();

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