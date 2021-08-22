using SyncStreamAPI.Enums;
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
        public DreckVideo currentVideo { get { return _currentVideo; } set { _currentVideo = value; currenttime = 0; } }
        public List<DreckVideo> playlist { get; set; } = new List<DreckVideo>();
        public List<Member> members { get; set; } = new List<Member>();
        public List<Member> bannedMembers { get; set; } = new List<Member>();
        public List<ChatMessage> chatmessages { get; set; } = new List<ChatMessage>();
        public string RoomId { get { return members.Count > 0 ? members[0].RoomId : ""; } }
        public string GallowWord { get; set; }
        public Language GameLanguage { get; set; }
        private bool _PlayingGallows { get; set; }
        public bool PlayingGallows { get { return _PlayingGallows; } set { if (_PlayingGallows == true && value == false) GallowGameEnded?.Invoke(this); _PlayingGallows = value; if (value == true) { GallowTime = GameLength; } } }
        private int _GallowTimer { get; set; }
        public int GallowTime { get { return _GallowTimer; } set { _GallowTimer = value; if (GallowTime > 0) GallowTimerUpdate?.Invoke(value, this); else GallowTimerElapsed?.Invoke(value, this); } }
        private int _GameLength { get; set; } = Helper.General.GallowGameLength;
        public int GameLength { get { return _GameLength; } set { _GameLength = value > 300 ? 300 : value < 60 ? 60 : value; } }

        public delegate void TimeUpdate(int Time, Server server);
        public event TimeUpdate GallowTimerUpdate;

        public delegate void TimerElapsed(int Time, Server server);
        public event TimerElapsed GallowTimerElapsed;

        public delegate void GameEnded(Server server);
        public event GameEnded GallowGameEnded;

        public delegate void WordUpdate(Server server);
        public event WordUpdate GallowWordUpdate;

        public Server(ServerData.DataManager _manager)
        {
            currentVideo = new DreckVideo() { title = "Nothing playing", url = "", ended = true };
            isplaying = false;
            currenttime = 0;
            _manager.GallowEvents(this);
            GallowCountdown();
        }

        public async void GallowCountdown()
        {
            await Task.Delay(1000);
            if (GallowTime > 0 && PlayingGallows && members.Count > 0)
                GallowTime -= 1;
            GallowCountdown();
        }

        public void UpdateGallowWord(bool EndGame)
        {
            if (GallowTime != GameLength)
                GallowTime = GameLength;
            if (EndGame)
                GallowTimerElapsed?.Invoke(0, this);
            GallowWord = Helper.General.GetGallowWord(GameLanguage);
            GallowWordUpdate?.Invoke(this);
        }

    }
}
