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
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token,
        TokenPosition = 1)]
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

            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x =>
                x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
            // Check if the user is authenticated and has the necessary privileges
            if (!dbFile.Temporary && dbUser == null)
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
            if (dbFile.Temporary)
            {
                Response.Headers.Add("Content-Disposition", $"inline;filename={dbFile.Name}{dbFile.FileEnding}");
                return File(fileStream, contentType);
            }

            return File(fileStream, contentType,
                dbFile.Name.EndsWith(dbFile.FileEnding) ? dbFile.Name : dbFile.Name + dbFile.FileEnding, true);
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
                        .Where(x => x.Height >= 360 && x.Height <= 2160)
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

    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = int.MaxValue)]
    [DisableRequestSizeLimit]
    [HttpPost("[action]")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task<IActionResult> addFile(string token)
    {
        // Create a list to track uploaded files during this session
        var uploadFiles = new List<string>();
        try
        {
            // Check if the request is valid and contains any files
            if (Request.Form?.Files?.Count == 0) return Unauthorized();

            // Get the user from the database and verify their token
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x =>
                x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token));
            var Token = dbUser?.RememberTokens.SingleOrDefault(x => x.Token == token);
            if (Token == null) return Unauthorized();

            // Iterate through all the uploaded files
            foreach (var file in Request.Form.Files)
            {
                // Check if the file is valid
                if (file.Length <= 0) continue; // Skip empty files

                // Create a new DbFile object and save the file to disk
                var fileInfo = new FileInfo(file.FileName);
                var dbFile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser);
                var path = Path.Combine(General.FilePath, $"{dbFile.FileKey}{dbFile.FileEnding}");
                Directory.CreateDirectory(General.FilePath);
                // Add the temporary file path to the list of uploaded files
                uploadFiles.Add(path);
                await using (var fileStream = System.IO.File.Create(path))
                {
                    await file.CopyToAsync(fileStream);
                }

                // Add the DbFile object to the database and save changes
                var savedFile = _postgres.Files?.Add(dbFile);
                await _postgres.SaveChangesAsync();
                await _hub.Clients.Group(dbUser.ApiKey).updateFolders(new FileDto(dbFile));
                uploadFiles.Remove(path);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        // If any files have not been completely uploaded, remove any temporary files not saved to DB
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
            if (Request.ContentLength <= 0 || Request.Form == null || Request.Form.Files == null ||
                Request.Form.Files.Count <= 0) return Unauthorized();

            // Validate API key against DbUsers API key
            var dbUser = _postgres.Users.SingleOrDefault(u => u.ApiKey == apiKey);
            if (dbUser == null) return Unauthorized();

            // Save file to temporary location
            var file = Request.Form.Files[0];
            var fileInfo = new FileInfo(file.FileName);
            var dbfile = new DbFile(Path.GetFileNameWithoutExtension(fileInfo.Name), fileInfo.Extension, dbUser, true);
            var path = General.TemporaryFilePath + $"/{dbfile.FileKey}{dbfile.FileEnding}";
            if (!Directory.Exists(General.TemporaryFilePath)) Directory.CreateDirectory(General.TemporaryFilePath);

            using (var ms = System.IO.File.OpenWrite(path))
            {
                await file.CopyToAsync(ms);
            }

            // Save new DbFile object to database
            var savedFile = _postgres.Files?.Add(dbfile);
            await _postgres.SaveChangesAsync();
            await _hub.Clients.Group(dbUser.ApiKey).updateFolders(new FileDto(dbfile));
            // Return Ok response code
            return Ok(new { fileKey = dbfile.Name, fileId = dbfile.FileKey });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}