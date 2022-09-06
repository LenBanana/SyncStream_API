using SyncStreamAPI.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class ChatMessage
    {
        public string message { get; set; }
        public string username { get; set; }
        public DateTime time { get; set; }
        public string color { get; set; }
        public string usercolor { get; set; }
        public ChatMessage()
        {
            time = DateTime.Now;
        }
    }

    public class SystemMessage : ChatMessage
    {
        public SystemMessage(string msg)
        {
            username = "System";
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
}
