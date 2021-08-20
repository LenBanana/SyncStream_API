using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class Server
    {
        public double _currenttime { get; set; }
        public double currenttime { get { return (playlist.Count > 0 ? _currenttime : 0); } set { _currenttime = value; } }
        public bool isplaying { get; set; }
        public string title { get { return playlist.Count > 0 ? playlist[0].title : "Nothing playing"; } }
        DreckVideo _currentVideo { get; set; }
        public DreckVideo currentVideo { get { return _currentVideo; } set { _currentVideo = value; currenttime = 0; PlayingGallows = false; } }
        public List<DreckVideo> playlist { get; set; } = new List<DreckVideo>();
        public List<Member> members { get; set; } = new List<Member>();
        public List<Member> bannedMembers { get; set; } = new List<Member>();
        public List<ChatMessage> chatmessages { get; set; } = new List<ChatMessage>();
        public string RoomId { get { return members.Count > 0 ? members[0].RoomId : ""; } }
        public string GallowWord { get; set; }
        private bool _PlayingGallows { get; set; }
        public bool PlayingGallows { get { return _PlayingGallows; } set { _PlayingGallows = value; if (value == true) { GallowTimer = Helper.General.GallowGameLength; GallowCountdown(); } } }
        private int _GallowTimer { get; set; } = Helper.General.GallowGameLength;
        private int GallowTimer { get { return _GallowTimer; } set { _GallowTimer = value; if (GallowTimer > 0) GallowTimerUpdate?.Invoke(value, this); else GallowTimerElapsed?.Invoke(value, this); } }

        public delegate void TimeUpdate(int Time, Server server);
        public event TimeUpdate GallowTimerUpdate;

        public delegate void TimerElapsed(int Time, Server server);
        public event TimerElapsed GallowTimerElapsed;

        public Server(ServerData.DataManager _manager)
        {
            currentVideo = new DreckVideo() { title = "Nothing playing", url = "", ended = true };
            isplaying = false;
            currenttime = 0;
            _manager.GallowTimeElapsed(this);
            _manager.GallowTimeUpdate(this);
        }

        public async void GallowCountdown()
        {
            await Task.Delay(1000);
            if (GallowTimer > 0 && PlayingGallows)
                GallowTimer -= 1;
            GallowCountdown();
        }

        public void UpdateGallowWord()
        {
            GallowWord = Helper.General.GetGallowWord();
            GallowTimer = Helper.General.GallowGameLength;
        }

    }
}
