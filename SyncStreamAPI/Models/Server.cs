using System.Collections.Generic;

namespace SyncStreamAPI.Models;

public class Server
{
    public Server()
    {
        bannedMembers = new List<Member>();
        playlist = new List<DreckVideo>();
        members = new List<Member>();
        chatmessages = new List<ChatMessage>();
        currentVideo = new DreckVideo { title = "Nothing playing", url = "", ended = true };
        isplaying = false;
        currenttime = 0;
    }

    public double _currenttime { get; set; }

    public double currenttime
    {
        get => playlist.Count > 0 ? _currenttime : 0;
        set => _currenttime = value;
    }

    public bool isplaying { get; set; }
    public string title => playlist.Count > 0 ? playlist[0].title : "Nothing playing";
    private DreckVideo _currentVideo { get; set; }

    public DreckVideo currentVideo
    {
        get => _currentVideo;
        set
        {
            _currentVideo = value;
            currenttime = 0;
        }
    }

    public List<DreckVideo> playlist { get; set; } = new();
    public List<Member> members { get; set; } = new();
    public List<Member> bannedMembers { get; set; } = new();
    public List<ChatMessage> chatmessages { get; set; } = new();
    public string RoomId => members.Count > 0 ? members[0]?.RoomId : "";
}