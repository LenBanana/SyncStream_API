using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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
    private const long MaxUploadBytes = 100 * 1024 * 1024; // 100 MB
    private readonly IConfiguration _configuration;
    private readonly PostgresContext _postgres;

    public ApplicationVersionController(PostgresContext postgres, IConfiguration configuration)
    {
        _postgres = postgres;
        _configuration = configuration;
    }

    private string StoragePath =>
        _configuration["AppVersionsStoragePath"] ?? AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>
    /// Returns the latest version metadata for the given application.
    /// Public — no authentication required.
    /// </summary>
    [HttpGet("version")]
    public IActionResult GetVersion(string appName)
    {
        var safeName = Path.GetFileName(appName);
        if (string.IsNullOrWhiteSpace(safeName))
            return BadRequest("Invalid appName.");

        var record = _postgres.AppVersions.FirstOrDefault(x => x.Name == safeName);
        if (record == null) return NotFound();

        var filePath = Path.Combine(StoragePath, safeName);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        var checkSum = Encryption.SHA256CheckSum(filePath);
        var downloadUrl = Url.Action(nameof(DownloadUpdate), "ApplicationVersion",
            new { appName = safeName }, Request.Scheme);

        return Ok(new
        {
            record.Version,
            CheckSum = checkSum,
            record.ReleaseNotes,
            record.LastUpdate,
            DownloadUrl = downloadUrl
        });
    }

    /// <summary>
    /// Downloads the application binary.
    /// Public — integrity is guaranteed by the SHA-256 in the version response.
    /// </summary>
    [HttpGet("download")]
    public async Task<IActionResult> DownloadUpdate(string appName)
    {
        var safeName = Path.GetFileName(appName);
        if (string.IsNullOrWhiteSpace(safeName))
            return BadRequest("Invalid appName.");

        if (_postgres.AppVersions.SingleOrDefault(x => x.Name == safeName) == null) return NotFound();

        var filePath = Path.Combine(StoragePath, safeName);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        var memory = new MemoryStream();
        await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await stream.CopyToAsync(memory);
        }

        memory.Position = 0;
        return File(memory, "application/octet-stream", safeName);
    }

    /// <summary>
    /// Uploads a new version of an application binary.
    /// Requires Administrator API key.
    /// </summary>
    [HttpPost("upload")]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.API,
        TokenPosition = 1)]
    public async Task<IActionResult> UploadUpdate(IFormFile file, string apiKey, string appName,
        string version = "0.1", string? releaseNotes = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file provided.");

        if (file.Length > MaxUploadBytes)
            return BadRequest($"File exceeds the maximum allowed size of {MaxUploadBytes / 1024 / 1024} MB.");

        var safeName = Path.GetFileName(appName);
        if (string.IsNullOrWhiteSpace(safeName) ||
            !safeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return BadRequest("appName must be a .exe filename.");

        if (!Directory.Exists(StoragePath))
            Directory.CreateDirectory(StoragePath);

        var targetPath = Path.Combine(StoragePath, safeName);

        // Write to a temp file first so a failed upload doesn't corrupt the existing release
        var tempPath = targetPath + ".tmp";
        try
        {
            await using (var fs = System.IO.File.OpenWrite(tempPath))
            {
                await file.CopyToAsync(fs);
            }

            System.IO.File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
            throw;
        }

        var record = _postgres.AppVersions?.SingleOrDefault(x => x.Name == safeName);
        if (record == null)
        {
            _postgres.AppVersions?.Add(new DbApplicationVersion
            {
                LastUpdate = DateTime.UtcNow,
                Name = safeName,
                Version = version,
                ReleaseNotes = releaseNotes
            });
        }
        else
        {
            record.Version = version;
            record.LastUpdate = DateTime.UtcNow;
            record.ReleaseNotes = releaseNotes;
        }

        await _postgres.SaveChangesAsync();
        return Ok(new { SuccessMessage = $"Successfully uploaded version {version}." });
    }
}
