namespace SyncStreamAPI.Models.WebRTC;

/// <summary>A voice call participant as everyone else in the call sees them.</summary>
public class VoipParticipantDto
{
    /// <summary>The participant's connection id, which is also the peer id on their SFU producers.</summary>
    public string ParticipantId { get; set; } = string.Empty;
    public string ParticipantName { get; set; } = string.Empty;
    public bool IsMuted { get; set; }
    public bool IsVideoMuted { get; set; }
    public bool IsScreenMuted { get; set; }
}
