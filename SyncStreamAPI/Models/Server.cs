using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class Server
    {
        public double _currenttime { get; set; }
        public double currenttime { get { return (ytURLs.Count > 0 ? _currenttime : 0); } set { _currenttime = value; } }
        public bool isplaying { get; set; }
        public string title { get; set; }
        YTVideo _ytURL { get; set; }
        public YTVideo ytURL { get { return _ytURL; } set { _ytURL = value; currenttime = 0; } }
        public List<YTVideo> ytURLs { get; set; } = new List<YTVideo>();
        public List<Member> members { get; set; } = new List<Member>();
        public List<Member> bannedMembers { get; set; } = new List<Member>();
        public List<ChatMessage> chatmessages { get; set; } = new List<ChatMessage>();

        public Server()
        {
            title = "Nothing playing";
            ytURL = new YTVideo() { title = "Nothing playing", url = "", ended = true };
            isplaying = false;
            currenttime = 0;
        }

    }
}
