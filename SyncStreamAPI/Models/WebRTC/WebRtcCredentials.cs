namespace SyncStreamAPI.Models.WebRTC;

public class WebRtcCredentials
{
    public WebRtcCredentials(string username, string password, string stunServer, string turnServer, long expiresAtUnixSec)
    {
        Username = username;
        Password = password;
        StunServer = stunServer;
        TurnServer = turnServer;
        ExpiresAtUnixSec = expiresAtUnixSec;
    }

    public string Username { get; set; }
    public string Password { get; set; }
    public string StunServer { get; set; }
    public string TurnServer { get; set; }
    /** Unix timestamp (seconds) at which these credentials expire. */
    public long ExpiresAtUnixSec { get; set; }
}