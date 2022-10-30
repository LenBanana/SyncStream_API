using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace SyncStreamAPI.Controllers
{
    [EnableCors("CORSPolicy")]
    [Route("api/Server")]
    public class ServerController : Controller
    {
        private IHubContext<ServerHub, IServerHub> _hub;
        PostgresContext _postgres;
        IConfiguration Configuration { get; }

        public ServerController(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres)
        {
            Configuration = configuration;
            _hub = hub;
            _postgres = postgres;
        }

        [HttpGet("[action]")]
        public IActionResult GetMessages(string UniqueId)
        {
            Room room = DataManager.GetRoom(UniqueId);
            if (room == null)
                return StatusCode(204);
            _hub.Clients.Group(UniqueId).sendmessages(room.server.chatmessages);
            return Ok(new { Message = "Request Completed" });
        }

        [HttpGet("[action]")]
        public IActionResult GetDbFile(int UniqueId, string Token)
        {
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == Token)).FirstOrDefault();
            if (dbUser == null)
                return StatusCode(StatusCodes.Status403Forbidden);
            if (dbUser.userprivileges >= 3)
            {
                var file = _postgres.Files.FirstOrDefault(x => x.ID == UniqueId);
                if (file == null)
                    return StatusCode(StatusCodes.Status404NotFound);
                return new FileContentResult(file.VideoFile, "application/octet-stream");
            }
            else
                return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
