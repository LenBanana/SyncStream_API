namespace SyncStreamAPI.Models.WebRTC
{
    public class WebRtcClientOffer
    {
        public string type { get; set; }
        public string sdp { get; set; }
        public string? ViewerId { get; set; }
    }

}
