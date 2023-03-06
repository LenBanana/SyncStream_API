using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;

namespace SyncStreamAPI.Controllers
{
    [EnableCors("CORSPolicy")]
    [Route("api/video")]
    public class VideoController : Controller
    {
        private IHubContext<ServerHub, IServerHub> _hub;
        PostgresContext _postgres;
        IConfiguration Configuration { get; }
        CancellationTokenSource source = new CancellationTokenSource();

        public VideoController(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres)
        {
            Configuration = configuration;
            _hub = hub;
            _postgres = postgres;
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> videoByToken(int uniqueId, string videoKey, string token)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                DbRememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
                if (Token == null || dbUser == null || dbUser?.userprivileges < UserPrivileges.Approved)
                {
                    if (dbUser != null)
                        await _hub.Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(Enums.AlertTypes.Danger) { Question = "You do not have permissions to view this content", Answer1 = "Ok" });
                    return StatusCode(StatusCodes.Status403Forbidden);
                }
                var dbFile = _postgres.Files?.Where(x => x.FileKey != null && x.FileKey == videoKey && x.ID == uniqueId).FirstOrDefault();
                if (dbFile == null)
                    return StatusCode(StatusCodes.Status403Forbidden);
                var path = General.FilePath + $"/{dbFile.FileKey}{dbFile.FileEnding}";
                if (!System.IO.File.Exists(path))
                    return StatusCode(StatusCodes.Status404NotFound);
                var fileReturn = System.IO.File.OpenRead(path);
                return File(fileReturn, "application/octet-stream", dbFile.Name.EndsWith(dbFile.FileEnding) ? dbFile.Name : dbFile.Name + dbFile.FileEnding, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> getYoutubeQuality(string url, string token)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                DbRememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
                if (Token == null || dbUser == null || dbUser?.userprivileges < UserPrivileges.Approved)
                {
                    if (dbUser != null)
                        await _hub.Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(Enums.AlertTypes.Danger) { Question = "You do not have permissions to view this content", Answer1 = "Ok" });
                    return StatusCode(StatusCodes.Status403Forbidden);
                }
                var ytdl = new YoutubeDL();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    ytdl.YoutubeDLPath = "/app/yt-dlp";
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    ytdl.YoutubeDLPath = "yt-dlp.exe";
                else return StatusCode(StatusCodes.Status403Forbidden);
                RunResult<VideoData> data = await ytdl.RunVideoDataFetch(url);
                if (data != null)
                {
                    return Ok(data.Data.Formats.Where(x => x.Height >= 360 && x.Height <= 2160).Select(x => x.Height).Distinct());
                }
                else
                    return StatusCode(StatusCodes.Status404NotFound);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = int.MaxValue)]
        [DisableRequestSizeLimit]
        [HttpPost("[action]")]
        public async Task<IActionResult> addVideo(string token)
        {
            try
            {
                if (Request.ContentLength <= 0 || Request.Form == null || Request.Form.Files == null || Request.Form.Files.Count <= 0)
                    return StatusCode(StatusCodes.Status403Forbidden);
                var file = Request.Form.Files[0];
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                DbRememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
                if (Token == null || dbUser == null || dbUser?.userprivileges < UserPrivileges.Administrator)
                    return StatusCode(StatusCodes.Status403Forbidden);
                if (file.Length <= 0)
                    return StatusCode(StatusCodes.Status405MethodNotAllowed);
                var fileInfo = new FileInfo(file.FileName);
                var dbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser);
                var path = General.FilePath + $"/{dbfile.FileKey}{dbfile.FileEnding}";
                if (!Directory.Exists(General.FilePath))
                    Directory.CreateDirectory(General.FilePath);
                using (var ms = System.IO.File.OpenWrite(path))
                {
                    await file.CopyToAsync(ms);
                }
                _postgres.Files?.Add(dbfile);
                await _postgres.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
