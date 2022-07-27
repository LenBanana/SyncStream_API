using SyncStreamAPI.Enums;
using SyncStreamAPI.Models.GameModels.Members;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models.GameModels.Gallows
{
    public class GallowLogic
    {
        public string RoomId { get; set; }
        public string GallowWord { get; set; }
        public List<GallowMember> members { get; set; } = new List<GallowMember>();
        public List<Drawing> drawings { get; set; } = new List<Drawing>();
        public Language GameLanguage { get; set; }
        private bool _PlayingGallows { get; set; } = true;
        public bool PlayingGallows { get { return _PlayingGallows; } set { if (_PlayingGallows == true && value == false) GallowGameEnded?.Invoke(this); _PlayingGallows = value; if (value == true) { GallowTime = GameLength; } } }
        private int _GallowTimer { get; set; }
        public int GallowTime { get { return _GallowTimer; } set { _GallowTimer = value; if (GallowTime > 0) GallowTimerUpdate?.Invoke(value, this); else GallowTimerElapsed?.Invoke(value, this); } }
        private int _GameLength { get; set; } = Helper.General.GallowGameLength;
        public int GameLength { get { return _GameLength; } set { _GameLength = value > Helper.General.GallowGameLengthMax ? Helper.General.GallowGameLengthMax : value < Helper.General.GallowGameLengthMin ? Helper.General.GallowGameLengthMin : value; } }

        public delegate void TimeUpdate(int Time, GallowLogic server);
        public event TimeUpdate GallowTimerUpdate;

        public delegate void TimerElapsed(int Time, GallowLogic server);
        public event TimerElapsed GallowTimerElapsed;

        public delegate void GameEnded(GallowLogic server);
        public event GameEnded GallowGameEnded;

        public delegate void WordUpdate(GallowLogic server);
        public event WordUpdate GallowWordUpdate;

        public GallowLogic(Games.Gallows.GallowGameManager _manager, string roomId, List<GallowMember> members)
        {
            this.members = members;
            this.members.ForEach(x => x.Kicked += X_Kicked);
            RoomId = roomId;
            _manager.GallowEvents(this);
            GallowCountdown();
            UpdateGallowWord(false);
        }

        private void X_Kicked(Member e)
        {
            var idx = this.members.FindIndex(x => x.ConnectionId == e.ConnectionId);
            if (idx >= 0)
            {
                e.Kicked -= X_Kicked;
                this.members.RemoveAt(idx);
            }
        }

        public void AddMember(Member member)
        {
            member.Kicked += X_Kicked;
            members.Add(new GallowMember(member));
        }

        public async void GallowCountdown()
        {
            await Task.Delay(1000);
            if (GallowTime > 0 && PlayingGallows)
            {
                GallowTime -= 1;
                GallowCountdown();
            }
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
