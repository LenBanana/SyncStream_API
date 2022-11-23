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

        public RtmpController(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres)
        {
            Configuration = configuration;
            _hub = hub;
            _postgres = postgres;
        }

        [HttpPost("[action]")]
        public IActionResult onpublish([FromBody] RtmpData rtmpData)
        {
            try
            {
                var dbUser = _postgres.Users?.FirstOrDefault(x => x.StreamToken != null && x.StreamToken.Token == rtmpData.token && x.username.ToLower() == rtmpData.name.ToLower());
                DbRememberToken Token = dbUser?.StreamToken;
                if (Token == null || dbUser.userprivileges < UserPrivileges.Approved)
                    return StatusCode(StatusCodes.Status403Forbidden);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("[action]")]
        public IActionResult onplay([FromBody] RtmpData rtmpData)
        {
            try
            {
                if (_postgres.Users?.FirstOrDefault(x => x.username.ToLower() == rtmpData.name.ToLower()) == null)
                    return StatusCode(StatusCodes.Status404NotFound);
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == rtmpData.token)).FirstOrDefault();
                DbRememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == rtmpData.token);
                if (Token == null || dbUser.userprivileges < UserPrivileges.Approved)
                    return StatusCode(StatusCodes.Status403Forbidden);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
