namespace SyncStreamAPI.DTOModel;

public class SponsorBlockSegmentDto
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public int Votes { get; set; }
    public bool Locked { get; set; }
    public string Description { get; set; } = string.Empty;
}