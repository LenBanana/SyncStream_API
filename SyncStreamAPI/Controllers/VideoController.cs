using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using YoutubeDLSharp.Options;
using Path = System.IO.Path;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/video")]
public class VideoController : Controller
{
    private readonly IContentTypeProvider _contentTypeProvider;
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly PostgresContext _postgres;
    private readonly IRoomStreamService _roomStreamService;

    public VideoController(
        IHubContext<ServerHub, IServerHub> hub,
        PostgresContext postgres,
        IContentTypeProvider contentTypeProvider,
        IRoomStreamService roomStreamService)
    {
        _hub = hub;
        _postgres = postgres;
        _contentTypeProvider = contentTypeProvider;
        _roomStreamService = roomStreamService;
    }

    [HttpGet("[action]")]
    public async Task<IActionResult> fileByToken(string fileKey, string? token)
    {
        try
        {
            var dbFile = _postgres.Files?.FirstOrDefault(x => x.FileKey == fileKey);
            if (dbFile == null)
                return StatusCode(StatusCodes.Status404NotFound, "The requested file could not be found");

            if (!dbFile.Public)
            {
                var dbUser = _postgres.Users?
                    .Include(x => x.RememberTokens)
                    .FirstOrDefault(x => x.RememberTokens != null &&
                                         x.RememberTokens.Any(y => y.Token == token));
                if (dbUser == null)
                    return Unauthorized("You do not have permissions to view this content");
            }

            var generalPath = dbFile.Temporary ? General.TemporaryFilePath : General.FilePath;
            var path = Path.Combine(generalPath, $"{dbFile.FileKey}{dbFile.FileEnding}");

            if (!System.IO.File.Exists(path))
                return StatusCode(StatusCodes.Status404NotFound, "The requested file could not be found on disk");

            var fileName = dbFile.Name.EndsWith(dbFile.FileEnding)
                ? dbFile.Name
                : dbFile.Name + dbFile.FileEnding;

            if (!_contentTypeProvider.TryGetContentType(path, out var contentType))
                contentType = "application/octet-stream";

            var isEmbeddable = contentType.StartsWith("image/") ||
                               contentType.StartsWith("video/") ||
                               contentType.StartsWith("audio/");

            var dispositionType = isEmbeddable ? "inline" : "attachment";
            var contentDisposition = new ContentDispositionHeaderValue(dispositionType);
            contentDisposition.SetHttpFileName(fileName);
            Response.Headers[HeaderNames.ContentDisposition] = contentDisposition.ToString();

            var fileStream = System.IO.File.OpenRead(path);
            return new FileStreamResult(fileStream, contentType)
            {
                EnableRangeProcessing = true,
                LastModified = System.IO.File.GetLastWriteTimeUtc(path)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError,
                "An error occurred while processing the request");
        }
    }

    [HttpGet("[action]")]
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token,
        TokenPosition = 1)]
    public async Task<IActionResult> getYoutubeQuality(string url, string token)
    {
        try
        {
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x =>
                x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
            var tokenObj = dbUser?.RememberTokens.SingleOrDefault(x => x.Token == token);
            if (tokenObj == null)
            {
                if (dbUser == null) return Unauthorized();
                const string errorMessage = "You do not have permissions to view this content";
                await _hub.Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(AlertType.Danger)
                    { Question = errorMessage, Answer1 = "Ok" });
                return Unauthorized(errorMessage);
            }

            var ytdl = General.GetYoutubeDl();
            var data = await ytdl.RunVideoDataFetch(url, overrideOptions: new OptionSet
            {
                ForceIPv4 = true
            });

            switch (data)
            {
                case { Data: not null }:
                {
                    var qualityOptions = data.Data.Formats
                        .Where(x => x.Height is >= 360 and <= 2160)
                        .Select(x => x.Height)
                        .Distinct()
                        .ToList();

                    return Ok(qualityOptions);
                }
                default:
                    return StatusCode(StatusCodes.Status404NotFound,
                        "Video data could not be fetched for the provided URL");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError,
                "An error occurred while processing the request");
        }
    }

    [DisableRequestSizeLimit]
    [HttpPost("[action]")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    [RequestSizeLimit(long.MaxValue)]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> addFile(string token)
    {
        var uploadFiles = new List<string>();

        try
        {
            if (Request.Form.Files.Count == 0)
                return Unauthorized();

            var dbUser = _postgres.Users
                .Include(x => x.RememberTokens)
                .FirstOrDefault(x => x.RememberTokens.Any(y => y.Token == token));
            if (dbUser == null)
                return Unauthorized();

            var dbToken = dbUser.RememberTokens.SingleOrDefault(x => x.Token == token);
            if (dbToken == null)
                return Unauthorized();

            foreach (var file in Request.Form.Files)
            {
                if (file.Length <= 0)
                    continue;

                var fileInfo = new FileInfo(file.FileName);
                var dbFile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser);
                var path = Path.Combine(General.FilePath, $"{dbFile.FileKey}{dbFile.FileEnding}");

                Directory.CreateDirectory(General.FilePath);
                uploadFiles.Add(path);

                try
                {
                    const int bufferSize = 81920;
                    await using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write,
                                     FileShare.None, bufferSize, useAsync: true))
                    {
                        await file.CopyToAsync(fileStream);
                    }

                    _postgres.Files?.Add(dbFile);
                    await _postgres.SaveChangesAsync();
                    await _hub.Clients.Group(dbUser.ApiKey).updateFolders(new FileDto(dbFile));
                    uploadFiles.Remove(path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    System.IO.File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        foreach (var path in uploadFiles) System.IO.File.Delete(path);
        return Ok();
    }

    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = int.MaxValue)]
    [DisableRequestSizeLimit]
    [HttpPost("[action]")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.API)]
    public async Task<IActionResult> uploadTemporaryImageFile(string apiKey)
    {
        try
        {
            if (Request.ContentLength <= 0 || Request.Form is not { Files.Count: > 0 }) return Unauthorized();

            var dbUser = _postgres.Users.SingleOrDefault(u => u.ApiKey == apiKey);
            if (dbUser == null) return Unauthorized();

            var file = Request.Form.Files[0];
            var fileInfo = new FileInfo(file.FileName);
            var dbFile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser, true);
            var path = General.TemporaryFilePath + $"/{dbFile.FileKey}{dbFile.FileEnding}";
            if (!Directory.Exists(General.TemporaryFilePath)) Directory.CreateDirectory(General.TemporaryFilePath);

            await using (var ms = System.IO.File.OpenWrite(path))
            {
                await file.CopyToAsync(ms);
            }

            _postgres.Files.Add(dbFile);
            await _postgres.SaveChangesAsync();
            await _hub.Clients.Group(dbUser.ApiKey).updateFolders(new FileDto(dbFile));
            return Ok(new { fileKey = dbFile.Name, fileId = dbFile.FileKey });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [DisableRequestSizeLimit]
    [HttpPost("roomUpload/start")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task<IActionResult> startRoomUpload(string token, string uniqueId, string name, string fileEnding,
        long totalSize)
    {
        var result = await _roomStreamService.StartRoomUploadAsync(token, uniqueId, name, fileEnding, totalSize);
        if (string.IsNullOrWhiteSpace(result.ErrorMessage))
            return StatusCode(result.StatusCode, result);

        return StatusCode(result.StatusCode, result);
    }

    [DisableRequestSizeLimit]
    [HttpPut("roomUpload/chunk")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task<IActionResult> uploadRoomChunk(string token, string uploadId, long startByte)
    {
        var result = await _roomStreamService.UploadRoomChunkAsync(Request, token, uploadId, startByte);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("roomUpload/status")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task<IActionResult> roomUploadStatus(string token, string uploadId)
    {
        var result = await _roomStreamService.GetRoomUploadStatusAsync(token, uploadId);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("roomUpload/complete")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task<IActionResult> completeRoomUpload(string token, string uploadId)
    {
        var result = await _roomStreamService.CompleteRoomUploadAsync(token, uploadId, Request.Scheme, Request.Host);
        return StatusCode(result.StatusCode, result);
    }

    [HttpDelete("roomUpload/cancel")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task<IActionResult> cancelRoomUpload(string token, string uploadId)
    {
        var result = await _roomStreamService.CancelRoomUploadAsync(token, uploadId);
        return StatusCode(result.StatusCode, result);
    }

    [DisableRequestSizeLimit]
    [HttpPost("[action]")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task<IActionResult> streamToRoom(string token, string uniqueId, string name, string fileEnding)
    {
        var result = await _roomStreamService.StreamToRoomAsync(Request, token, uniqueId, name, fileEnding);
        if (result.StatusCode == StatusCodes.Status200OK)
            return Ok(new { fileKey = result.FileKey });

        return string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? StatusCode(result.StatusCode)
            : StatusCode(result.StatusCode, result.ErrorMessage);
    }

    [HttpGet("[action]/{fileKey}/{fileName}")]
    public IActionResult hlsSegment(string fileKey, string fileName)
    {
        var result = _roomStreamService.GetHlsSegment(fileKey, fileName);
        if (result.StatusCode != StatusCodes.Status200OK || result.Stream == null || string.IsNullOrWhiteSpace(result.ContentType))
            return string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? StatusCode(result.StatusCode)
                : StatusCode(result.StatusCode, result.ErrorMessage);

        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        if (result.DisableCache)
        {
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
        }
        else
        {
            Response.Headers["Cache-Control"] = "no-cache";
        }

        return new FileStreamResult(result.Stream, result.ContentType);
    }
}
