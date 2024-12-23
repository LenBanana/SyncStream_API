﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/version")]
public class ApplicationVersionController : Controller
{
    private readonly PostgresContext _postgres;

    public ApplicationVersionController(PostgresContext postgres)
    {
        _postgres = postgres;
    }

    [HttpGet("version")]
    public IActionResult GetVersion(string appName)
    {
        var latestVersion = _postgres.AppVersions.FirstOrDefault(x => x.Name == appName)?.Version;
        if (latestVersion == null) return NotFound();
        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, appName);
        if (!System.IO.File.Exists(filePath)) return NotFound();
        var checkSum = Encryption.SHA256CheckSum(filePath);
        return Ok(new { Version = latestVersion, CheckSum = checkSum });
    }

    [HttpGet("download")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.API)]
    public async Task<IActionResult> DownloadUpdate(string apiKey, string appName)
    {
        if (_postgres.AppVersions.SingleOrDefault(x => x.Name == appName) == null) return NotFound();
        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, appName);
        if (!System.IO.File.Exists(filePath)) return NotFound();
        var memory = new MemoryStream();
        await using (var stream = new FileStream(filePath, FileMode.Open))
        {
            await stream.CopyToAsync(memory);
        }

        memory.Position = 0;
        return File(memory, "application/octet-stream", Path.GetFileName(filePath));
    }

    [HttpPost("upload")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.API,
        TokenPosition = 1)]
    public async Task<IActionResult> UploadUpdate(IFormFile file, string apiKey, string appName, string version = "0.1")
    {
        if (file == null) return StatusCode(StatusCodes.Status403Forbidden);
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, appName);
        await using (var ms = System.IO.File.OpenWrite(path))
        {
            await file.CopyToAsync(ms);
        }

        var ver = _postgres.AppVersions?.SingleOrDefault(x => x.Name == appName);
        if (ver == null)
        {
            _postgres.AppVersions?.Add(new DbApplicationVersion
                { LastUpdate = DateTime.UtcNow, Name = appName, Version = version });
        }
        else
        {
            ver.Version = version;
            ver.LastUpdate = DateTime.UtcNow;
        }

        await _postgres.SaveChangesAsync();
        return Ok(new { SuccessMessage = "Successfully uploaded new version." });
    }
}