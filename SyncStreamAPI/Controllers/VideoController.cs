using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.PostgresModels;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Controllers
{
    [EnableCors("CORSPolicy")]
    [Route("api/video")]
    public class VideoController : Controller
    {
        private IHubContext<ServerHub, IServerHub> _hub;
        PostgresContext _postgres;
        IConfiguration Configuration { get; }

        public VideoController(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres)
        {
            Configuration = configuration;
            _hub = hub;
            _postgres = postgres;
        }

        [HttpGet("[action]")]
        public IActionResult videoByToken(int uniqueId, string token)
        {
            var dbFile = _postgres.Files?.Where(x => x.FileKey != null && x.FileKey == token && x.ID == uniqueId).FirstOrDefault();
            if (dbFile == null)
                return StatusCode(StatusCodes.Status403Forbidden);
            return File(dbFile.VideoFile, "application/octet-stream", dbFile.Name.EndsWith(dbFile.FileEnding) ? dbFile.Name : dbFile.Name + dbFile.FileEnding);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> addVideo(string token)
        {
            if (Request.ContentLength <= 0 || Request.Form == null || Request.Form.Files == null || Request.Form.Files.Count <= 0)
                return StatusCode(StatusCodes.Status403Forbidden);
            var file = Request.Form.Files[0];
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
            if (dbUser == null)
                return StatusCode(StatusCodes.Status403Forbidden);
            RememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
            if (Token == null)
                return StatusCode(StatusCodes.Status403Forbidden);
            if (dbUser.userprivileges >= 3)
            {
                if (file.Length <= 0)
                    return StatusCode(StatusCodes.Status405MethodNotAllowed);
                using (var ms = new System.IO.MemoryStream())
                {
                    await file.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    _postgres.Files?.Add(new DbFile(file.FileName, bytes, "." + file.FileName.Split('.').Last(), dbUser));
                    await _postgres.SaveChangesAsync();
                    return Ok();
                }
            }
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
