namespace SyncStreamAPI.Models.WebRTC;

public class WebRtcIceCandidate
{
    public string Candidate { get; set; }
    public string SdpMid { get; set; }
    public int? SdpMLineIndex { get; set; }
}