﻿using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Models;
using SyncStreamAPI.ServerData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Controllers
{
    [EnableCors("MyPolicy")]
    [Route("api/Server")]
    public class ServerController : Controller
    {
        private IHubContext<ServerHub> _hub;
        MariaContext _maria;
        public ServerController(IHubContext<ServerHub> hub, MariaContext maria)
        {
            _hub = hub;
            _maria = maria;
        }

        [HttpGet("[action]")]
        public IActionResult GetMessages(string UniqueId)
        {
            Room room = DataManager.GetRoom(UniqueId);
            if (room == null)
                return StatusCode(204);
            _hub.Clients.Group(UniqueId).SendAsync("sendmessage", room.server.chatmessages);
            return Ok(new { Message = "Request Completed" });
        }

        [HttpGet("[action]")]
        public IActionResult GetServer(string UniqueId)
        {
            Room room = DataManager.GetRoom(UniqueId);
            if (room == null)
                return StatusCode(204);
            _hub.Clients.Group(UniqueId).SendAsync("sendserver", room.server);

            return Ok(new { Message = "Request Completed" });
        }

        [HttpGet("[action]")]
        public IActionResult GetRooms()
        {
            _hub.Clients.All.SendAsync("getrooms", DataManager.GetRooms());

            return Ok(new { Message = "Request Completed" });
        }
    }
}
