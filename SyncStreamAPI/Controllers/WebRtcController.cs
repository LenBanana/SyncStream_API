using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;

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
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public IActionResult GetWebRtcCredentials(string token)
    {
        return Ok(General.GenerateTemporaryCredentials(Configuration));
    }
}