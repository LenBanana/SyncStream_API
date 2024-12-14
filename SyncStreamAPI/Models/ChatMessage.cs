using System;
using SyncStreamAPI.Helper;

namespace SyncStreamAPI.Models;

public class ChatMessage
{
    public ChatMessage()
    {
        time = DateTime.UtcNow;
    }

    public string message { get; set; }
    public string username { get; set; }
    public DateTime time { get; set; }
    public string color { get; set; }
    public string usercolor { get; set; }
}

public class SystemMessage : ChatMessage
{
    public SystemMessage(string msg)
    {
        username = General.SystemMessageName;
        color = Colors.SystemColor;
        usercolor = Colors.SystemUserColor;
        message = msg;
    }
}

public class WhisperUserMessage : ChatMessage
{
    public WhisperUserMessage()
    {
        color = Colors.WhisperMsgColor;
        usercolor = Colors.WhisperUserColor;
    }
}

public class WhisperReceiverMessage : ChatMessage
{
    public WhisperReceiverMessage()
    {
        color = Colors.WhisperMsgColor;
        usercolor = Colors.WhisperReceiverColor;
    }
}