using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyncStreamAPI.Auth;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper.SponsorBlock;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Wire.Playback;

namespace SyncStreamAPI.Controllers;

[EnableCors("CORSPolicy")]
[Route("api/sponsorblock")]
public class SponsorBlockController : ControllerBase
{
    private readonly ILogger<SponsorBlockController> _logger;
    private readonly PostgresContext _postgres;
    private readonly ISponsorBlockService _sponsorBlockService;

    public SponsorBlockController(
        ISponsorBlockService sponsorBlockService,
        PostgresContext postgres,
        ILogger<SponsorBlockController> logger)
    {
        _sponsorBlockService = sponsorBlockService;
        _postgres = postgres;
        _logger = logger;
    }

    /// <summary>
    ///     Which categories the server skips.
    /// </summary>
    /// <remarks>
    ///     Readable by anyone, including a guest, because any client may be the host and the host is the one that
    ///     performs a skip. It is a playback policy, not information about anybody.
    /// </remarks>
    [HttpGet("policy")]
    public async Task<IActionResult> GetPolicy()
    {
        var settings = await LoadSettingsAsync();
        return Ok(new SponsorBlockPolicyDto(settings.SponsorBlockSkipCategories, SponsorBlockCategories.All));
    }

    /// <summary>Sets which categories the server skips.</summary>
    [HttpPost("policy")]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<IActionResult> SetPolicy([FromBody] SponsorBlockPolicyDto policy)
    {
        // Sanitised rather than trusted: only categories the upstream API actually serves are stored, so an
        // unknown value cannot be saved and then handed to every client as though it meant something.
        var categories = SponsorBlockCategories.Sanitise(policy?.skipCategories);

        var settings = await LoadSettingsAsync();
        settings.SponsorBlockSkipCategories = categories;
        await _postgres.SaveChangesAsync();

        _logger.LogInformation("SponsorBlock skip categories set to {Categories}", string.Join(", ", categories));
        return Ok(new SponsorBlockPolicyDto(categories, SponsorBlockCategories.All));
    }

    /// <summary>
    ///     The single settings row, created if a database predates it.
    /// </summary>
    /// <remarks>
    ///     The row is seeded by the migration, so this only has to build one for a database that was migrated
    ///     before the seed existed, which is better than every reader having to handle its absence.
    /// </remarks>
    private async Task<DbServerSettings> LoadSettingsAsync()
    {
        var settings = await _postgres.ServerSettings
            .FirstOrDefaultAsync(row => row.ID == DbServerSettings.SingletonId);

        if (settings != null) return settings;

        settings = new DbServerSettings
        {
            ID = DbServerSettings.SingletonId,
            SponsorBlockSkipCategories = new List<string>(SponsorBlockCategories.Default)
        };
        _postgres.ServerSettings.Add(settings);
        await _postgres.SaveChangesAsync();
        return settings;
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