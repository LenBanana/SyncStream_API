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
using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.Auth;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/extension")]
public class ExtensionController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IContentTypeProvider _contentTypeProvider;
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly MainManager _manager;
    private readonly PostgresContext _postgres;

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
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task<IActionResult> AddVideoByExtension(string roomId, string videoUrl,
        string password = null)
    {
        var room = MainManager.GetRoom(roomId);
        if (room == null)
            return BadRequest("Room not found");
        if (!string.IsNullOrEmpty(room.password) && string.IsNullOrEmpty(password))
            return Unauthorized("Room is password protected");
        // Through the password hash, like the hub's join path. This was the second place a room password was
        // compared by string equality, and leaving it that way would have refused every correct password once
        // the stored form stopped being the value the caller sends.
        if (!string.IsNullOrEmpty(room.password)
            && PasswordHashing.Verify(room.password, password) == PasswordVerdict.NoMatch)
            return Unauthorized("Wrong password");
        var user = await _postgres.Users.FirstOrDefaultAsync(x => x.ID == this.CallerUserId());
        if (user == null)
            return BadRequest("User not found");
        var result = await RoomManager.AddVideo(new DreckVideo
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