﻿using System;
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

        public Server()
        {
            currentVideo = new DreckVideo() { title = "Nothing playing", url = "", ended = true };
            isplaying = false;
            currenttime = 0;
        }

    }
}
