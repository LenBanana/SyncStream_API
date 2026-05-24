using System;

namespace SyncStreamAPI.DTOModel;

public class SponsorBlockSegmentsResponseDto
{
    public string VideoId { get; set; } = string.Empty;
    public DateTimeOffset FetchedAtUtc { get; set; }
    public SponsorBlockSegmentDto[] Segments { get; set; } = Array.Empty<SponsorBlockSegmentDto>();
}