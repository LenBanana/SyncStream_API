using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.PostgresModels;
using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.Auth;

namespace SyncStreamAPI.Controllers;

/// <summary>
/// Serves finished SFU recording files stored in the shared recordings volume.
/// The volume is mounted read-only at /recordings (in Docker) or the path from
/// the RECORDINGS_DIR environment variable.
/// </summary>
[EnableCors("CORSPolicy")]
[Route("api/recordings")]
public class RecordingController : Controller
{
    private readonly IContentTypeProvider _contentTypeProvider;

    private static string RecordingsDir =>
        System.Environment.GetEnvironmentVariable("RECORDINGS_DIR") ?? "/recordings";

    public RecordingController(IContentTypeProvider contentTypeProvider)
    {
        _contentTypeProvider = contentTypeProvider;
    }

    /// <summary>Returns a list of all finished recording filenames. Admin only — use the filename from sfuRecordingStarted to download your own recording.</summary>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public IActionResult List()
    {
        if (!Directory.Exists(RecordingsDir))
            return Ok(new string[0]);

        var files = Directory.GetFiles(RecordingsDir, "*.mkv")
            .Select(f => Path.GetFileName(f))
            .OrderByDescending(f => f)
            .ToArray();

        return Ok(files);
    }

    /// <summary>Streams a specific recording file to the caller.</summary>
    [HttpGet("{filename}")]
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public IActionResult Download(string filename)
    {
        // Prevent path traversal attacks.
        if (string.IsNullOrEmpty(filename) || filename.Contains('/') || filename.Contains('\\') || filename.Contains(".."))
            return BadRequest("Invalid filename.");

        var filePath = Path.Combine(RecordingsDir, filename);
        if (!System.IO.File.Exists(filePath))
            return NotFound();

        if (!_contentTypeProvider.TryGetContentType(filename, out var contentType))
            contentType = "video/x-matroska";

        return PhysicalFile(filePath, contentType, filename, enableRangeProcessing: true);
    }

    /// <summary>Deletes a specific recording file.</summary>
    [HttpDelete("{filename}")]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public IActionResult Delete(string filename)
    {
        if (string.IsNullOrEmpty(filename) || filename.Contains('/') || filename.Contains('\\') || filename.Contains(".."))
            return BadRequest("Invalid filename.");

        var filePath = Path.Combine(RecordingsDir, filename);
        if (!System.IO.File.Exists(filePath))
            return NotFound();

        System.IO.File.Delete(filePath);
        return Ok(new { ok = true });
    }
}
