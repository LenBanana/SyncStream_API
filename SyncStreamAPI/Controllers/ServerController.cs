using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Controllers
{
    [EnableCors("CORSPolicy")]
    [Route("api/Server")]
    public class ServerController : Controller
    {
        private IHubContext<ServerHub, IServerHub> _hub;
        MariaContext _maria;
        IConfiguration Configuration { get; }
        public ServerController(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub, MariaContext maria)
        {
            Configuration = configuration;
            _hub = hub;
            _maria = maria;
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
        public IActionResult GetServer(string UniqueId)
        {
            Room room = DataManager.GetRoom(UniqueId);
            if (room == null)
                return StatusCode(204);
            _hub.Clients.Group(UniqueId).sendserver(room.server);

            return Ok(new { Message = "Request Completed" });
        }

        [HttpGet("[action]")]
        public IActionResult GetRooms()
        {
            _hub.Clients.All.getrooms(DataManager.GetRooms());

            return Ok(new { Message = "Request Completed" });
        }

        //[HttpGet("[action]")]
        //public async Task<IActionResult> TryYTApi(string url)
        //{
        //    var info = await General.NoEmbedYTApi(url);
        //    return Ok(info.Title);
        //}
    }
}
