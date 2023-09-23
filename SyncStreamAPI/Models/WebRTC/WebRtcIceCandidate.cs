namespace SyncStreamAPI.Models.WebRTC;

public class WebRtcIceCandidate
{
    public string ViewerId { get; set; }
    public string Candidate { get; set; }
    public string SdpMid { get; set; }
    public int? SdpMLineIndex { get; set; }
}

public class VoipIceCandidate
{
    public string ParticipantId { get; set; }
    public string ParticipantName { get; set; }
    public string Candidate { get; set; }
    public string SdpMid { get; set; }
    public int? SdpMLineIndex { get; set; }
    public string Foundation { get; set; }
    public int? Component { get; set; }
    public string Protocol { get; set; }
    public int? Priority { get; set; }
    public string Address { get; set; }
    public int? Port { get; set; }
    public string Type { get; set; }
    public string TcpType { get; set; }
    public string RelatedAddress { get; set; }
    public int? RelatedPort { get; set; }
    public string UsernameFragment { get; set; }
}