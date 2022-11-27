using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SyncStreamAPI.Controllers
{
    [EnableCors("CORSPolicy")]
    [Route("api/rtmp")]
    public class RtmpController : Controller
    {
        private IHubContext<ServerHub, IServerHub> _hub;
        PostgresContext _postgres;
        IConfiguration Configuration { get; }
        CancellationTokenSource source = new CancellationTokenSource();
        DataManager _manager;

        public RtmpController(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres, DataManager manager)
        {
            Configuration = configuration;
            _hub = hub;
            _postgres = postgres;
            _manager = manager;
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> onpublish([FromForm] RtmpData rtmpData)
        {
            try
            {
                var dbUser = _postgres.Users?.FirstOrDefault(x => x.StreamToken != null && x.StreamToken == rtmpData.token && x.username.ToLower() == rtmpData.name.ToLower());
                string Token = dbUser?.StreamToken;
                if (Token == null || Token.Length == 0 || dbUser.userprivileges < UserPrivileges.Approved)
                    return StatusCode(StatusCodes.Status403Forbidden);
                var liveUser = _manager.LiveUsers.FirstOrDefault(x => x.id == rtmpData.token);
                if (liveUser == null)
                {
                    _manager.LiveUsers.Add(new LiveUser() { userName = rtmpData.name, id = rtmpData.token });
                    var liveUsers = _manager.LiveUsers;
                    await _hub.Clients.Groups(General.LoggedInGroupName).getliveusers(liveUsers);
                }
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> onpublishdone([FromForm] RtmpData rtmpData)
        {
            try
            {
                var dbUser = _postgres.Users?.FirstOrDefault(x => x.StreamToken != null && x.StreamToken == rtmpData.token && x.username.ToLower() == rtmpData.name.ToLower());
                string Token = dbUser?.StreamToken;
                if (Token == null || Token.Length == 0 || dbUser.userprivileges < UserPrivileges.Approved)
                    return StatusCode(StatusCodes.Status403Forbidden);
                var liveUser = _manager.LiveUsers.FirstOrDefault(x => x.id == rtmpData.token);
                if (liveUser != null)
                {
                    _manager.LiveUsers.Remove(liveUser);
                    var liveUsers = _manager.LiveUsers;
                    await _hub.Clients.Groups(General.LoggedInGroupName).getliveusers(liveUsers);
                }
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> onplay([FromForm] RtmpData rtmpData)
        {
            try
            {
                var liveUser = _manager.LiveUsers?.FirstOrDefault(x => x.userName.ToLower() == rtmpData.name.ToLower());
                if (liveUser == null)
                    return StatusCode(StatusCodes.Status404NotFound);
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == rtmpData.token)).FirstOrDefault();
                if (dbUser == null || dbUser.userprivileges < UserPrivileges.Approved)
                    return StatusCode(StatusCodes.Status403Forbidden);
                liveUser.watchMember.Add(dbUser.ToDTO());
                var liveUsers = _manager.LiveUsers;
                await _hub.Clients.Groups(General.LoggedInGroupName).getliveusers(liveUsers);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> onplaydone([FromForm] RtmpData rtmpData)
        {
            try
            {
                var liveUser = _manager.LiveUsers?.FirstOrDefault(x => x.userName.ToLower() == rtmpData.name.ToLower());
                if (liveUser == null)
                    return StatusCode(StatusCodes.Status404NotFound);
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == rtmpData.token)).FirstOrDefault();
                if (dbUser == null || dbUser.userprivileges < UserPrivileges.Approved)
                    return StatusCode(StatusCodes.Status403Forbidden);
                var watchMemberIdx = liveUser.watchMember.FindIndex(x => x.ID == dbUser.ID);
                if (watchMemberIdx != -1)
                    liveUser.watchMember.RemoveAt(watchMemberIdx);
                var liveUsers = _manager.LiveUsers;
                await _hub.Clients.Groups(General.LoggedInGroupName).getliveusers(liveUsers);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
