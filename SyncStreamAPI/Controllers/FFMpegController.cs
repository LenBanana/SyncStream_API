using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using ScreenIT.Helper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.PostgresModels;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SyncStreamAPI.Controllers
{
    [EnableCors("CORSPolicy")]
    [Route("api/media")]
    public class FFMpegController : Controller
    {
        private readonly IHubContext<ServerHub, IServerHub> _hub;
        readonly PostgresContext _postgres;
        IConfiguration Configuration { get; }
        private readonly IContentTypeProvider _contentTypeProvider;
        readonly CancellationTokenSource source = new CancellationTokenSource();

        public FFMpegController(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres, IContentTypeProvider contentTypeProvider)
        {
            Configuration = configuration;
            _hub = hub;
            _postgres = postgres;
            _contentTypeProvider = contentTypeProvider;
        }

        [HttpGet("path")]
        public IActionResult GetFFmpegPath(string apiKey)
        {
            var dbUser = _postgres.Users.SingleOrDefault(u => u.ApiKey == apiKey);
            if (dbUser == null || dbUser.userprivileges < UserPrivileges.Administrator)
            {
                return Unauthorized();
            }
            return Ok(new { Path = General.GetFFmpegPath() });
        }

        [HttpPost("CutMedia")]
        public async Task<IActionResult> CutMedia(string apiKey, int startMilliSeconds, int endMilliSeconds)
        {
            var dbUser = _postgres.Users.SingleOrDefault(u => u.ApiKey == apiKey);
            if (dbUser == null || dbUser.userprivileges < UserPrivileges.Administrator)
            {
                return Unauthorized();
            }
            // Check if the request is valid and contains a file
            if (Request.ContentLength <= 0 || Request.Form == null || !Request.Form.Files.Any())
            {
                return Unauthorized();
            }
            var inputFile = Request.Form.Files[0];
            if (startMilliSeconds >= endMilliSeconds)
                return StatusCode(StatusCodes.Status422UnprocessableEntity, new { Error = "Endtime has to be greater than starttime" });
            var startTime = TimeSpan.FromMilliseconds(startMilliSeconds);
            var endTime = TimeSpan.FromMilliseconds(endMilliSeconds);
            var fileInfo = new FileInfo(inputFile.FileName);
            return await ProcessMedia(inputFile, dbUser, (inputPath, outputPath) => FFMpegTools.CutMedia(inputPath, outputPath, startTime, endTime), fileInfo.Extension, inputFile.ContentType);
        }

        [HttpPost("ConvertMedia")]
        public async Task<IActionResult> ConvertMedia(string apiKey, MediaType mediaType)
        {
            var dbUser = _postgres.Users.SingleOrDefault(u => u.ApiKey == apiKey);
            if (dbUser == null || dbUser.userprivileges < UserPrivileges.Administrator)
            {
                return Unauthorized();
            }
            // Check if the request is valid and contains a file
            if (Request.ContentLength <= 0 || Request.Form == null || !Request.Form.Files.Any())
            {
                return Unauthorized();
            }
            var inputFile = Request.Form.Files[0];
            var fileInfo = new FileInfo(inputFile.FileName);
            var mimeType = MimeTypeHelper.GetMimeType(inputFile);
            switch (mediaType)
            {
                case MediaType.MP3:
                case MediaType.WAV:
                case MediaType.OGG:
                case MediaType.FLAC:
                case MediaType.AIFF:
                case MediaType.M4A:
                    return await ProcessMedia(inputFile, dbUser, (inputPath, outputPath) => FFMpegTools.ConvertAudio(inputPath, outputPath, mediaType.ToString()), $".{mediaType}", mimeType);
                case MediaType.MP4:
                case MediaType.AVI:
                case MediaType.WMV:
                case MediaType.MOV:
                case MediaType.MKV:
                    return await ProcessMedia(inputFile, dbUser, (inputPath, outputPath) => FFMpegTools.ConvertVideo(inputPath, outputPath), $".{mediaType}", mimeType);
                case MediaType.GIF:
                    return await ProcessMedia(inputFile, dbUser, (inputPath, outputPath) => FFMpegTools.ConvertToGif(inputPath, outputPath), ".gif", "image/gif");
                case MediaType.PNG:
                case MediaType.JPEG:
                case MediaType.BMP:
                case MediaType.TIFF:
                case MediaType.WEBP:
                    return ConvertFile(inputFile, mediaType);
                default:
                    return StatusCode(StatusCodes.Status405MethodNotAllowed);
            }
        }

        private IActionResult ConvertFile(IFormFile file, MediaType mediaType)
        {
            // Convert the file to the output format
            IImageEncoder encoder = null;
            string mimeType = "image/png";
            var fileName = file.FileName;
            fileName = Path.ChangeExtension(fileName, $".{mediaType.ToString().ToLower()}");
            switch (mediaType)
            {
                case MediaType.PNG:
                    encoder = new PngEncoder();
                    break;
                case MediaType.JPEG:
                    encoder = new JpegEncoder();
                    mimeType = "image/jpeg";
                    break;
                case MediaType.BMP:
                    encoder = new BmpEncoder();
                    mimeType = "image/bmp";
                    break;
                case MediaType.WEBP:
                    encoder = new WebpEncoder();
                    mimeType = "image/webp";
                    break;
                case MediaType.TIFF:
                    encoder = new TiffEncoder();
                    mimeType = "image/tiff";
                    break;
            }
            using (var image = Image.Load(file.OpenReadStream()))
            {
                var ms = new MemoryStream();
                image.Save(ms, encoder);
                ms.Position = 0;
                Response.Headers.Add("Content-Disposition", $"attachment;filename={fileName}");
                return File(ms, mimeType);
            }
        }

        private async Task<IActionResult> ProcessMedia(IFormFile inputFile, DbUser dbUser, Func<string, string, Task<string>> mediaOperation, string extension, string mimeType)
        {
            try
            {
                var fileInfo = new FileInfo(inputFile.FileName);
                var dbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser);
                var path = Path.Combine(General.TemporaryFilePath, $"{dbfile.FileKey}{dbfile.FileEnding}");
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await inputFile.CopyToAsync(stream);
                }

                var outputDbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), extension, dbUser, temporary: true);
                outputDbfile.Created = DateTime.UtcNow.AddDays(-General.DaysToKeepImages).AddMinutes(General.MinutesToKeepFFmpeg);
                var outputPath = Path.Combine(General.TemporaryFilePath, $"{outputDbfile.FileKey}{extension}");

                var result = await mediaOperation(path, outputPath);
                FileCheck.CheckOverrideFile(path);
                if (result != null)
                {
                    var fileBytes = await System.IO.File.ReadAllBytesAsync(outputPath);
                    _postgres.Files?.Add(outputDbfile);
                    await _postgres.SaveChangesAsync();
                    Response.Headers.Add("Content-Disposition", $"attachment;filename={outputDbfile.Name}{outputDbfile.FileEnding}");
                    return File(fileBytes, mimeType);
                }
                else
                {
                    return NotFound();
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
