using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.PostgresModels;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SyncStreamAPI.Controllers
{
    [EnableCors("CORSPolicy")]
    [Route("api/version")]
    public class ApplicationVersionController : Controller
    {
        private readonly IHubContext<ServerHub, IServerHub> _hub;
        readonly PostgresContext _postgres;
        IConfiguration Configuration { get; }
        private readonly IContentTypeProvider _contentTypeProvider;
        readonly CancellationTokenSource source = new CancellationTokenSource();

        public ApplicationVersionController(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres, IContentTypeProvider contentTypeProvider)
        {
            Configuration = configuration;
            _hub = hub;
            _postgres = postgres;
            _contentTypeProvider = contentTypeProvider;
        }

        [HttpGet("version")]
        public IActionResult GetVersion(string appName)
        {
            var latestVersion = _postgres.AppVersions.FirstOrDefault(x => x.Name == appName)?.Version;
            if (latestVersion == null)
            {
                return NotFound();
            }
            return Ok(new { Version = latestVersion });
        }

        [HttpGet("download")]
        public async Task<IActionResult> DownloadUpdate(string apiKey, string appName)
        {
            try
            {
                if (_postgres.AppVersions.SingleOrDefault(x => x.Name == appName) == null)
                {
                    return NotFound();
                }
                var dbUser = _postgres.Users.SingleOrDefault(u => u.ApiKey == apiKey);
                if (dbUser == null || dbUser.userprivileges < UserPrivileges.Administrator)
                {
                    return Unauthorized();
                }
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, appName);
                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound();
                }
                var memory = new MemoryStream();
                using (var stream = new FileStream(filePath, FileMode.Open))
                {
                    await stream.CopyToAsync(memory);
                }
                memory.Position = 0;
                return File(memory, "application/octet-stream", Path.GetFileName(filePath));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadUpdate(IFormFile file, string apiKey, string appName, string version = "0.1")
        {
            try
            {
                var dbUser = _postgres.Users?.Where(x => x.ApiKey == apiKey).FirstOrDefault();
                if (dbUser == null || dbUser.userprivileges < UserPrivileges.Administrator)
                {
                    return StatusCode(StatusCodes.Status403Forbidden);
                }
                if (file == null)
                {
                    return StatusCode(StatusCodes.Status403Forbidden);
                }
                var fileInfo = new FileInfo(file.FileName);
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, appName);
                using (var ms = System.IO.File.OpenWrite(path))
                {
                    await file.CopyToAsync(ms);
                }
                var ver = _postgres.AppVersions?.SingleOrDefault(x => x.Name == appName);
                if (ver == null)
                {
                    _postgres.AppVersions.Add(new DbApplicationVersion() { LastUpdate = DateTime.Now, Name = appName, Version = version });
                }
                else
                {
                    ver.Version = version;
                    ver.LastUpdate = DateTime.Now;
                }
                await _postgres.SaveChangesAsync();
                return Ok(new { SuccessMessage = "Successfully uploaded new version." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
