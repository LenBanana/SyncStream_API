using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/audio")]
public class AudioController : Controller
{
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly PostgresContext _postgres;

    public AudioController(IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres)
    {
        _hub = hub;
        _postgres = postgres;
    }

    [HttpGet("wave")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.API)]
    public IActionResult GetAudioWaveform(string apiKey, string fileKey)
    {
        var dbFile = _postgres.Files.FirstOrDefault(x => x.FileKey == fileKey);
        if (dbFile == null) return StatusCode(StatusCodes.Status404NotFound);
        var fileName = $"{dbFile.FileKey}{dbFile.FileEnding}";
        var path = Path.Combine(dbFile.Temporary ? General.TemporaryFilePath : General.FilePath, fileName);
        if (System.IO.File.Exists(path))
            return Ok(new { AudioBytes = NAudioTools.GetWaveform(path) });
        return StatusCode(StatusCodes.Status404NotFound);
    }
}