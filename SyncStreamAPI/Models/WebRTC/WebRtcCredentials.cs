namespace SyncStreamAPI.Models.WebRTC;

public class WebRtcCredentials
{
    public WebRtcCredentials(string username, string password)
    {
        Username = username;
        Password = password;
    }

    public string Username { get; set; }
    public string Password { get; set; }
}