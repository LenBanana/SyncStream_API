using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Helper.FFmpeg;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/media")]
public class FFMpegController : Controller
{
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly PostgresContext _postgres;

    public FFMpegController(IHubContext<ServerHub, IServerHub> hub, PostgresContext postgres)
    {
        _hub = hub;
        _postgres = postgres;
    }

    [HttpGet("path")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.API)]
    public IActionResult GetFFmpegPath(string apiKey)
    {
        return Ok(new { Path = General.GetFFmpegPath() });
    }

    [HttpPost("CutMedia")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.API)]
    public async Task<IActionResult> CutMedia(string apiKey, int startMilliSeconds, int endMilliSeconds)
    {
        var dbUser = _postgres.Users.First(u => u.ApiKey == apiKey);
        if (Request.ContentLength <= 0 || Request.Form == null || !Request.Form.Files.Any()) return BadRequest();
        var inputFile = Request.Form.Files[0];
        if (startMilliSeconds >= endMilliSeconds)
            return StatusCode(StatusCodes.Status422UnprocessableEntity,
                new { Error = "Endtime has to be greater than starttime" });
        var startTime = TimeSpan.FromMilliseconds(startMilliSeconds);
        var endTime = TimeSpan.FromMilliseconds(endMilliSeconds);
        var fileInfo = new FileInfo(inputFile.FileName);
        var baseFunction = FFmpegFunction.GetDefaultFunction(fileInfo, fileInfo.Extension, dbUser);
        var cutFunction = new FFmpegCutMedia(baseFunction.InputPath, baseFunction.OutputPath, startTime, endTime, null,
            baseFunction.InputFile, baseFunction.OutputFile);
        return await FFmpegTools.ProcessMedia(inputFile, dbUser, cutFunction, inputFile.ContentType, _postgres, _hub);
    }

    [HttpPost("ConvertMedia")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.API)]
    public async Task<IActionResult> ConvertMedia(string apiKey, MediaType mediaType)
    {
        if (Request.ContentLength <= 0 || Request.Form == null || !Request.Form.Files.Any()) return BadRequest();
        var dbUser = _postgres.Users.First(u => u.ApiKey == apiKey);
        var inputFile = Request.Form.Files[0];
        var fileInfo = new FileInfo(inputFile.FileName);
        var mimeType = MimeTypeHelper.GetMimeType(inputFile);
        var baseFunction = FFmpegFunction.GetDefaultFunction(fileInfo, $".{mediaType.ToString().ToLower()}", dbUser);
        switch (mediaType)
        {
            case MediaType.MP3:
            case MediaType.WAV:
            case MediaType.OGG:
            case MediaType.FLAC:
            case MediaType.AIFF:
            case MediaType.M4A:
                var convertAudio = new FFmpegConvertAudio(baseFunction.InputPath, baseFunction.OutputPath,
                    baseFunction.InputFile, baseFunction.OutputFile);
                return await FFmpegTools.ProcessMedia(inputFile, dbUser, convertAudio, mimeType, _postgres, _hub);
            case MediaType.MP4:
            case MediaType.WEBM:
            case MediaType.FLV:
            case MediaType.AVI:
            case MediaType.WMV:
            case MediaType.MOV:
            case MediaType.MKV:
                var convertVideo = new FFmpegConvertVideo(baseFunction.InputPath, baseFunction.OutputPath,
                    baseFunction.InputFile, baseFunction.OutputFile);
                return await FFmpegTools.ProcessMedia(inputFile, dbUser, convertVideo, mimeType, _postgres, _hub);
            case MediaType.GIF:
                var convertGIF = new FFmpegConvertGIF(baseFunction.InputPath, baseFunction.OutputPath,
                    baseFunction.InputFile, baseFunction.OutputFile);
                return await FFmpegTools.ProcessMedia(inputFile, dbUser, convertGIF, "image/gif", _postgres, _hub);
            case MediaType.PNG:
            case MediaType.PDF:
            case MediaType.JPEG:
            case MediaType.BMP:
            case MediaType.TIFF:
            case MediaType.WEBP:
                return await ImageTools.ConvertFile(inputFile, mediaType, dbUser, _postgres, _hub);
            default:
                return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }
    }
}