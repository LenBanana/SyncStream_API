using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Interfaces;

namespace SyncStreamAPI.Helper.SponsorBlock;

public class SponsorBlockService : ISponsorBlockService
{
    private const string SponsorBlockApiBaseUrl = "https://sponsor.ajay.app/api/";
    private static readonly Regex VideoIdRegex = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] SupportedCategories =
    {
        "sponsor",
        "selfpromo",
        "interaction",
        "intro",
        "outro",
        "preview",
        "filler",
        "music_offtopic",
        "hook",
        "exclusive_access"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<SponsorBlockService> _logger;

    public SponsorBlockService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        ILogger<SponsorBlockService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<SponsorBlockSegmentsResponseDto> GetSegmentsAsync(
        string videoId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidVideoId(videoId))
            throw new ArgumentException("videoId must be a valid 11 character YouTube ID.", nameof(videoId));

        var cacheKey = $"sponsorblock:segments:{videoId}";
        if (_memoryCache.TryGetValue(cacheKey, out SponsorBlockSegmentsResponseDto? cached) && cached != null)
            return cached;

        var response = await FetchSegmentsAsync(videoId, cancellationToken);
        var cacheDuration = response.Segments.Length == 0 ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(15);
        _memoryCache.Set(cacheKey, response, cacheDuration);
        return response;
    }

    private async Task<SponsorBlockSegmentsResponseDto> FetchSegmentsAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        var hashPrefix = ComputeHashPrefix(videoId);
        var categories = Uri.EscapeDataString(JsonSerializer.Serialize(SupportedCategories));
        var actionTypes = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { "skip" }));
        var requestUrl =
            $"{SponsorBlockApiBaseUrl}skipSegments/{hashPrefix}?categories={categories}&actionTypes={actionTypes}&service=YouTube";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        using var client = _httpClientFactory.CreateClient(nameof(SponsorBlockService));
        client.Timeout = TimeSpan.FromSeconds(10);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return EmptyResponse(videoId);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "SponsorBlock request failed for {VideoId} with status {StatusCode}: {Body}",
                videoId,
                (int)response.StatusCode,
                body);
            throw new HttpRequestException("SponsorBlock request failed.", null, response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var candidates = await JsonSerializer.DeserializeAsync<SponsorBlockCandidateDto[]>(
            responseStream,
            SerializerOptions,
            cancellationToken) ?? Array.Empty<SponsorBlockCandidateDto>();

        var matchingVideo = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.VideoId, videoId, StringComparison.Ordinal));

        if (matchingVideo?.Segments == null || matchingVideo.Segments.Length == 0)
            return EmptyResponse(videoId);

        var segments = matchingVideo.Segments
            .Where(IsValidSegment)
            .OrderBy(segment => segment.Segment![0])
            .ThenBy(segment => segment.Segment![1])
            .Select(MapSegment)
            .ToArray();

        return new SponsorBlockSegmentsResponseDto
        {
            VideoId = videoId,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Segments = segments
        };
    }

    private static bool IsValidVideoId(string videoId)
    {
        return !string.IsNullOrWhiteSpace(videoId) && VideoIdRegex.IsMatch(videoId);
    }

    private static bool IsValidSegment(SponsorBlockApiSegmentDto segment)
    {
        if (segment.Segment == null || segment.Segment.Length < 2)
            return false;

        var start = segment.Segment[0];
        var end = segment.Segment[1];
        return double.IsFinite(start)
               && double.IsFinite(end)
               && end > start
               && !string.IsNullOrWhiteSpace(segment.UUID)
               && !string.IsNullOrWhiteSpace(segment.Category)
               && !string.IsNullOrWhiteSpace(segment.ActionType);
    }

    private static string ComputeHashPrefix(string videoId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(videoId));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return hex[..4];
    }

    private static SponsorBlockSegmentDto MapSegment(SponsorBlockApiSegmentDto segment)
    {
        return new SponsorBlockSegmentDto
        {
            Id = segment.UUID!,
            Category = segment.Category!,
            ActionType = segment.ActionType!,
            StartTime = segment.Segment![0],
            EndTime = segment.Segment[1],
            Votes = segment.Votes,
            Locked = segment.Locked != 0,
            Description = segment.Description ?? string.Empty
        };
    }

    private static SponsorBlockSegmentsResponseDto EmptyResponse(string videoId)
    {
        return new SponsorBlockSegmentsResponseDto
        {
            VideoId = videoId,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Segments = Array.Empty<SponsorBlockSegmentDto>()
        };
    }

    private sealed class SponsorBlockCandidateDto
    {
        [JsonPropertyName("videoID")]
        public string? VideoId { get; set; }

        [JsonPropertyName("segments")]
        public SponsorBlockApiSegmentDto[]? Segments { get; set; }
    }

    private sealed class SponsorBlockApiSegmentDto
    {
        [JsonPropertyName("segment")]
        public double[]? Segment { get; set; }

        [JsonPropertyName("UUID")]
        public string? UUID { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("actionType")]
        public string? ActionType { get; set; }

        [JsonPropertyName("locked")]
        public int Locked { get; set; }

        [JsonPropertyName("votes")]
        public int Votes { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}