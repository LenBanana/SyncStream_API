namespace SyncStreamAPI.Models.WebRTC
{
    public class WebRtcClientOffer
    {
        public string type { get; set; }
        public string sdp { get; set; }
        public string? ViewerId { get; set; }
    }

    public class VoipOffer
    {
        public string ParticipantName { get; set; }
        public string Type { get; set; } // "offer" or "answer"
        public string Sdp { get; set; } // Session description
    }
    
    public class VoipParticipantDto
    {
        public string ParticipantId { get; set; }
        public string ParticipantName { get; set; }
    }
}