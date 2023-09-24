using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Helper.Youtube;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/youtube")]
public class YoutubeController : Controller
{
    private readonly IConfiguration Configuration;
    
    public YoutubeController(IConfiguration configuration)
    {
        Configuration = configuration;
    }
    
    [HttpGet("YoutubeSearch")]
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task<IActionResult> YoutubeSearch(string token, string query, string pageToken = "", string order = "relevance", int pageSize = 10)
    {
        var result = await new YoutubeApi(Configuration).Search(query, pageToken, order: order, pageSize: pageSize);
        return Ok(result);
    }
}