using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Helper;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/extension")]
public class ExtensionController : Controller
{
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly PostgresContext _postgres;
    private readonly IContentTypeProvider _contentTypeProvider;
    private readonly MainManager _manager;
    private readonly IConfiguration _configuration;

    public ExtensionController(IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres,
        IContentTypeProvider contentTypeProvider, MainManager manager, IConfiguration configuration)
    {
        _hub = hub;
        _postgres = postgres;
        _contentTypeProvider = contentTypeProvider;
        _manager = manager;
        _configuration = configuration;
    }

    [HttpPost("[action]")]
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.API)]
    public async Task<IActionResult> AddVideoByExtension(string apiKey, string roomId, string videoUrl, string password = null)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null)
            return BadRequest("Room not found");
        if (!string.IsNullOrEmpty(room.password) && string.IsNullOrEmpty(password))
            return Unauthorized("Room is password protected");
        if (room.password != password)
            return BadRequest("Wrong password");
        var user = await _postgres.Users.FirstOrDefaultAsync(x => x.ApiKey == apiKey);
        if (user == null)
            return BadRequest("User not found");
        var result = await RoomManager.AddVideo(new DreckVideo()
        {
            AddedBy = user.username,
            ended = false,
            title = await General.ResolveUrl(videoUrl, _configuration),
            url = videoUrl
        }, roomId);
        if (string.IsNullOrEmpty(result))
            return Ok();
        return BadRequest(result);
    }
}