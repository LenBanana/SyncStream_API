using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Games.Gallows;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models.GameModels.Members;

namespace SyncStreamAPI.Models.GameModels.Gallows;

public class GallowLogic
{
    public delegate void GameEnded(GallowLogic server);

    public delegate void TimerElapsed(int Time, GallowLogic server);

    public delegate void TimeUpdate(int Time, GallowLogic server);

    public delegate void WordUpdate(GallowLogic server);

    public GallowLogic(GallowGameManager _manager, string roomId, List<GallowMember> members)
    {
        this.members = members;
        this.members.ForEach(x => x.Kicked += X_Kicked);
        RoomId = roomId;
        _manager.GallowEvents(this);
        GallowCountdown();
        UpdateGallowWord(false);
    }

    public string RoomId { get; set; }
    public string GallowWord { get; set; }
    public List<GallowMember> members { get; set; } = new();
    public List<Drawing> drawings { get; set; } = new();
    public Language GameLanguage { get; set; }
    private bool _PlayingGallows { get; set; } = true;

    public bool PlayingGallows
    {
        get => _PlayingGallows;
        set
        {
            if (_PlayingGallows && value == false) GallowGameEnded?.Invoke(this);
            _PlayingGallows = value;
            if (value) GallowTime = GameLength;
        }
    }

    private int _GallowTimer { get; set; }

    public int GallowTime
    {
        get => _GallowTimer;
        set
        {
            _GallowTimer = value;
            if (GallowTime > 0)
                GallowTimerUpdate?.Invoke(value, this);
            else
                GallowTimerElapsed?.Invoke(value, this);
        }
    }

    private int _GameLength { get; set; } = General.GallowGameLength;

    public int GameLength
    {
        get => _GameLength;
        set => _GameLength = value > General.GallowGameLengthMax ? General.GallowGameLengthMax :
            value < General.GallowGameLengthMin ? General.GallowGameLengthMin : value;
    }

    public event TimeUpdate GallowTimerUpdate;
    public event TimerElapsed GallowTimerElapsed;
    public event GameEnded GallowGameEnded;
    public event WordUpdate GallowWordUpdate;

    private void X_Kicked(Member e)
    {
        var idx = members.FindIndex(x => x.ConnectionId == e.ConnectionId);
        if (idx >= 0)
        {
            e.Kicked -= X_Kicked;
            members.RemoveAt(idx);
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
        if (GallowTime != GameLength) GallowTime = GameLength;

        if (EndGame) GallowTimerElapsed?.Invoke(0, this);

        GallowWord = General.GetGallowWord(GameLanguage);
        GallowWordUpdate?.Invoke(this);
    }
}