using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
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
        private readonly IContentTypeProvider _contentTypeProvider;
        CancellationTokenSource source = new CancellationTokenSource();

        public VideoController(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres, IContentTypeProvider contentTypeProvider)
        {
            Configuration = configuration;
            _hub = hub;
            _postgres = postgres;
            _contentTypeProvider = contentTypeProvider;
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> fileByToken(string fileKey, string token)
        {
            try
            {
                // Fetch the file record from the database based on the unique ID and video key
                var dbFile = _postgres.Files?.FirstOrDefault(x => x.FileKey == fileKey);
                if (dbFile == null)
                {
                    // If the file record is not found, return a 404 error with a specific error message
                    var errorMessage = "The requested file could not be found";
                    return StatusCode(StatusCodes.Status404NotFound, errorMessage);
                }

                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
                // Check if the user is authenticated and has the necessary privileges
                if (!dbFile.Temporary && (dbUser == null || dbUser.userprivileges < UserPrivileges.Approved))
                {
                    // If the user is not authorized to view the content, return a 403 error and display an error message
                    if (dbUser != null)
                        await _hub.Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(Enums.AlertTypes.Danger) { Question = "You do not have permissions to view this content", Answer1 = "Ok" });
                    return StatusCode(StatusCodes.Status403Forbidden, "You do not have permissions to view this content");
                }

                // Check if the file exists on disk
                var generalPath = dbFile.Temporary ? General.TemporaryFilePath : General.FilePath;
                var path = Path.Combine(generalPath, $"{dbFile.FileKey}{dbFile.FileEnding}");
                if (!System.IO.File.Exists(path))
                {
                    // If the file does not exist on disk, return a 404 error with a specific error message
                    var errorMessage = "The requested file could not be found on disk";
                    await _hub.Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(Enums.AlertTypes.Danger) { Question = errorMessage, Answer1 = "Ok" });
                    return StatusCode(StatusCodes.Status404NotFound, errorMessage);
                }

                // Return the file as a stream
                var fileStream = System.IO.File.OpenRead(path);
                var contentType = _contentTypeProvider.TryGetContentType(path, out var contentTypeResult) ? contentTypeResult : "application/octet-stream";
                if (dbFile.Temporary)
                {
                    Response.Headers.Add("Content-Disposition", "inline");
                    return File(fileStream, contentType);
                }
                return File(fileStream, contentType, dbFile.Name.EndsWith(dbFile.FileEnding) ? dbFile.Name : dbFile.Name + dbFile.FileEnding, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                var errorMessage = "An error occurred while processing the request";
                return StatusCode(StatusCodes.Status500InternalServerError, errorMessage);
            }
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> getYoutubeQuality(string url, string token)
        {
            try
            {
                // Check if the user is authenticated and has the necessary privileges
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
                var tokenObj = dbUser?.RememberTokens.SingleOrDefault(x => x.Token == token);
                if (tokenObj == null || dbUser == null || dbUser.userprivileges < UserPrivileges.Approved)
                {
                    // If the user is not authorized to view the content, return a 403 error and display an error message
                    if (dbUser != null)
                    {
                        var errorMessage = "You do not have permissions to view this content";
                        await _hub.Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(Enums.AlertTypes.Danger) { Question = errorMessage, Answer1 = "Ok" });
                        return StatusCode(StatusCodes.Status403Forbidden, errorMessage);
                    }
                    return StatusCode(StatusCodes.Status403Forbidden);
                }

                // Fetch the video data for the provided YouTube URL using the YouTube DL library
                var ytdl = General.GetYoutubeDL();
                RunResult<VideoData> data = await ytdl.RunVideoDataFetch(url);

                // If the video data was fetched successfully, return a list of available video quality options
                if (data != null)
                {
                    var qualityOptions = data.Data.Formats
                        .Where(x => x.Height >= 360 && x.Height <= 2160)
                        .Select(x => x.Height)
                        .Distinct()
                        .ToList();

                    return Ok(qualityOptions);
                }
                else
                {
                    // If the video data could not be fetched, return a 404 error with a specific error message
                    var errorMessage = "Video data could not be fetched for the provided URL";
                    return StatusCode(StatusCodes.Status404NotFound, errorMessage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                var errorMessage = "An error occurred while processing the request";
                return StatusCode(StatusCodes.Status500InternalServerError, errorMessage);
            }
        }

        [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = int.MaxValue)]
        [DisableRequestSizeLimit]
        [HttpPost("[action]")]
        public async Task<IActionResult> addFile(string token)
        {
            try
            {
                // Check if the request is valid and contains a file
                if (Request.ContentLength <= 0 || Request.Form == null || !Request.Form.Files.Any())
                    return StatusCode(StatusCodes.Status403Forbidden);

                // Get the user from the database and verify their token
                var file = Request.Form.Files[0];
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
                var Token = dbUser?.RememberTokens.SingleOrDefault(x => x.Token == token);
                if (Token == null || dbUser == null || dbUser.userprivileges < UserPrivileges.Administrator)
                    return StatusCode(StatusCodes.Status403Forbidden);

                // Check if the file is valid
                if (file.Length <= 0)
                    return StatusCode(StatusCodes.Status405MethodNotAllowed);

                // Create a new DbFile object and save the file to disk
                var fileInfo = new FileInfo(file.FileName);
                var dbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser);
                var path = Path.Combine(General.FilePath, $"{dbfile.FileKey}{dbfile.FileEnding}");
                Directory.CreateDirectory(General.FilePath);
                using (var fileStream = System.IO.File.Create(path))
                    await file.CopyToAsync(fileStream);

                // Add the DbFile object to the database and save changes
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

        [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = int.MaxValue)]
        [DisableRequestSizeLimit]
        [HttpPost("[action]")]
        public async Task<IActionResult> uploadTemporaryImageFile(string apiKey)
        {
            try
            {
                // Ensure request has a file attached
                if (Request.ContentLength <= 0 || Request.Form == null || Request.Form.Files == null || Request.Form.Files.Count <= 0)
                    return StatusCode(StatusCodes.Status403Forbidden);

                // Validate API key against DbUsers API key
                var dbUser = _postgres.Users?.Where(x => x.ApiKey == apiKey).FirstOrDefault();
                if (dbUser == null || dbUser.userprivileges < UserPrivileges.Administrator)
                    return StatusCode(StatusCodes.Status403Forbidden);

                // Save file to temporary location
                var file = Request.Form.Files[0];
                var fileInfo = new FileInfo(file.FileName);
                var dbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser, temporary: true);
                var path = General.TemporaryFilePath + $"/{dbfile.FileKey}{dbfile.FileEnding}";
                if (!Directory.Exists(General.TemporaryFilePath))
                    Directory.CreateDirectory(General.TemporaryFilePath);
                using (var ms = System.IO.File.OpenWrite(path))
                {
                    await file.CopyToAsync(ms);
                }

                // Save new DbFile object to database
                var savedFile = _postgres.Files?.Add(dbfile);
                await _postgres.SaveChangesAsync();
                await _hub.Clients.Group(dbUser.ApiKey).updateFolders(new DTOModel.FileDto(savedFile.Entity));
                // Return Ok response code
                return Ok(new { fileKey = savedFile.Entity.Name, fileId = savedFile.Entity.FileKey });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
