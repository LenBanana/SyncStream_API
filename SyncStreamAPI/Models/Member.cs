﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Timers;
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models.GameModels.Members;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Models;

public class Member
{
    public delegate void KickEvent(Member e);

    private readonly Timer timer;

    public Member()
    {
        timer = new Timer(General.SecondsToKickMember.TotalMilliseconds);
        timer.Elapsed += Timer_Elapsed;
        timer.Start();
        MainManager.GetRoomManager().AddToMemberCheck(this);
    }

    [Key] public string username { get; set; }

    public string RoomId { get; set; }
    public string ConnectionId { get; set; }
    private string _uptime { get; set; } = DateTime.UtcNow.ToString("MM.dd.yyyy HH:mm:ss");

    public string uptime
    {
        get => _uptime;
        set
        {
            _uptime = value;
            timer.Stop();
            timer.Start();
        }
    }

    public bool ishost { get; set; }
    public Dictionary<string, List<string>> PrivateMessages { get; set; } = new();
    public event KickEvent Kicked;

    public MemberDTO ToDTO()
    {
        return new MemberDTO(username, ishost);
    }

    public GallowMember ToGallowMember()
    {
        return new GallowMember(this);
    }

    public BlackjackMember ToBlackjackMember(BlackjackManager manager)
    {
        return new BlackjackMember(this, manager);
    }

    private void Timer_Elapsed(object sender, ElapsedEventArgs e)
    {
        InvokeKick();
    }

    public void InvokeKick()
    {
        Kicked?.Invoke(this);
        timer.Stop();
        timer.Dispose();
    }

    public List<string> GetMessages(string User)
    {
        return PrivateMessages[User];
    }

    public string AddMessage(string User, string Message)
    {
        if (!PrivateMessages.ContainsKey(User)) PrivateMessages.Add(User, new List<string>());

        var FullMessage = string.Format("{0} {1}: {2}", DateTime.UtcNow.ToString("HH:mm"), username, Message);
        PrivateMessages[User].Add(FullMessage);
        if (PrivateMessages[User].Count > 150) PrivateMessages[User].RemoveAt(0);

        return FullMessage;
    }

    public void RemoveMessages(string User)
    {
        if (!PrivateMessages.ContainsKey(User)) PrivateMessages.Remove(User);
    }
}