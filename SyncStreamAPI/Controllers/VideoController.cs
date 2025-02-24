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

    public VideoController(IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres,
        IContentTypeProvider contentTypeProvider)
    {
        _hub = hub;
        _postgres = postgres;
        _contentTypeProvider = contentTypeProvider;
    }

    [HttpGet("[action]")]
    public async Task<IActionResult> fileByToken(string fileKey, string? token)
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

            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x =>
                x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
            // Check if the user is authenticated and has the necessary privileges
            if (!dbFile.Public && dbUser == null)
                // If the user is not authorized to view the content, return a 403 error and display an error message
                return Unauthorized("You do not have permissions to view this content");

            // Check if the file exists on disk
            var generalPath = dbFile.Temporary ? General.TemporaryFilePath : General.FilePath;
            var path = Path.Combine(generalPath, $"{dbFile.FileKey}{dbFile.FileEnding}");
            if (!System.IO.File.Exists(path))
            {
                // If the file does not exist on disk, return a 404 error with a specific error message
                var errorMessage = "The requested file could not be found on disk";
                await _hub.Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(AlertType.Danger)
                    { Question = errorMessage, Answer1 = "Ok" });
                return StatusCode(StatusCodes.Status404NotFound, errorMessage);
            }

            // Return the file as a stream
            var fileStream = System.IO.File.OpenRead(path);
            var contentType = _contentTypeProvider.TryGetContentType(path, out var contentTypeResult)
                ? contentTypeResult
                : "application/octet-stream";
            if (!dbFile.Temporary)
                return File(fileStream, contentType,
                    dbFile.Name.EndsWith(dbFile.FileEnding) ? dbFile.Name : dbFile.Name + dbFile.FileEnding, true);
            Response.Headers.Add("Content-Disposition", $"inline;filename={dbFile.Name}{dbFile.FileEnding}");
            return File(fileStream, contentType);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            var errorMessage = "An error occurred while processing the request";
            return StatusCode(StatusCodes.Status500InternalServerError, errorMessage);
        }
    }

    [HttpGet("[action]")]
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token,
        TokenPosition = 1)]
    public async Task<IActionResult> getYoutubeQuality(string url, string token)
    {
        try
        {
            // Check if the user is authenticated and has the necessary privileges
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x =>
                x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
            var tokenObj = dbUser?.RememberTokens.SingleOrDefault(x => x.Token == token);
            if (tokenObj == null)
            {
                // If the user is not authorized to view the content, return a 403 error and display an error message
                if (dbUser == null) return Unauthorized();
                const string errorMessage = "You do not have permissions to view this content";
                await _hub.Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(AlertType.Danger)
                    { Question = errorMessage, Answer1 = "Ok" });
                return Unauthorized(errorMessage);
            }

            // Fetch the video data for the provided YouTube URL using the YouTube DL library
            var ytdl = General.GetYoutubeDl();
            var data = await ytdl.RunVideoDataFetch(url, overrideOptions: new OptionSet
            {
                ForceIPv4 = true
            });
            switch (data)
            {
                // If the video data was fetched successfully, return a list of available video quality options
                case { Data: not null }:
                {
                    var qualityOptions = data.Data?.Formats
                        .Where(x => x.Height is >= 360 and <= 2160)
                        .Select(x => x.Height)
                        .Distinct()
                        .ToList();

                    return Ok(qualityOptions);
                }
                default:
                {
                    const string errorMessage = "Video data could not be fetched for the provided URL";
                    return StatusCode(StatusCodes.Status404NotFound, errorMessage);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            const string errorMessage = "An error occurred while processing the request";
            return StatusCode(StatusCodes.Status500InternalServerError, errorMessage);
        }
    }

    [DisableRequestSizeLimit]
    [HttpPost("[action]")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
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

                // Ensure the target directory exists
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
                    // Log the error and delete any partially uploaded file
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

        // Cleanup any files that were not completely processed
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
            // Ensure request has a file attached
            if (Request.ContentLength <= 0 || Request.Form is not { Files.Count: > 0 }) return Unauthorized();

            // Validate API key against DbUsers API key
            var dbUser = _postgres.Users.SingleOrDefault(u => u.ApiKey == apiKey);
            if (dbUser == null) return Unauthorized();

            // Save file to temporary location
            var file = Request.Form.Files[0];
            var fileInfo = new FileInfo(file.FileName);
            var dbFile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser, true);
            var path = General.TemporaryFilePath + $"/{dbFile.FileKey}{dbFile.FileEnding}";
            if (!Directory.Exists(General.TemporaryFilePath)) Directory.CreateDirectory(General.TemporaryFilePath);

            await using (var ms = System.IO.File.OpenWrite(path))
            {
                await file.CopyToAsync(ms);
            }

            // Save new DbFile object to database
            _postgres.Files.Add(dbFile);
            await _postgres.SaveChangesAsync();
            await _hub.Clients.Group(dbUser.ApiKey).updateFolders(new FileDto(dbFile));
            // Return Ok response code
            return Ok(new { fileKey = dbFile.Name, fileId = dbFile.FileKey });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}