using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;
using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.Auth;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/webrtc")]
public class WebRtcController : Controller
{
    private readonly IConfiguration Configuration;

    public WebRtcController(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    [HttpGet("GetWebRtcCredentials")]
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public IActionResult GetWebRtcCredentials()
    {
        return Ok(General.GenerateTemporaryCredentials(Configuration));
    }
}