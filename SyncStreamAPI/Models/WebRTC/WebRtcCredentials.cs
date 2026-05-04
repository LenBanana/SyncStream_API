namespace SyncStreamAPI.Models.WebRTC;

public class WebRtcCredentials
{
    public WebRtcCredentials(string username, string password, string stunServer, string turnServer)
    {
        Username = username;
        Password = password;
        StunServer = stunServer;
        TurnServer = turnServer;
    }

    public string Username { get; set; }
    public string Password { get; set; }
    public string StunServer { get; set; }
    public string TurnServer { get; set; }
}