using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SyncStreamAPI.Interfaces;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/sponsorblock")]
public class SponsorBlockController : ControllerBase
{
    private readonly ILogger<SponsorBlockController> _logger;
    private readonly ISponsorBlockService _sponsorBlockService;

    public SponsorBlockController(
        ISponsorBlockService sponsorBlockService,
        ILogger<SponsorBlockController> logger)
    {
        _sponsorBlockService = sponsorBlockService;
        _logger = logger;
    }

    [HttpGet("segments")]
    public async Task<IActionResult> GetSegments(string videoId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return BadRequest("videoId is required.");

        try
        {
            var response = await _sponsorBlockService.GetSegmentsAsync(videoId, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "SponsorBlock proxy failed for {VideoId}", videoId);
            return StatusCode(502, "SponsorBlock data is currently unavailable.");
        }
    }
}