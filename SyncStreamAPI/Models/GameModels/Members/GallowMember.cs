using SyncStreamAPI.DTOModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static SyncStreamAPI.Models.Member;

namespace SyncStreamAPI.Models.GameModels.Members
{
    public class GallowMember
    {
        public GallowMember(Member member)
        {
            member.Kicked += Member_Kicked;
            username = member.username;
            isDrawing = member.ishost;
            ConnectionId = member.ConnectionId;
        }

        private void Member_Kicked(Member e)
        {
            Kicked?.Invoke(e);
        }

        public event KickEvent Kicked;
        public string ConnectionId { get; set; }
        public string username { get; set; }
        public bool isDrawing { get; set; }
        public double gallowPoints { get; set; } = 0;
        public bool guessedGallow { get; set; } = false;
        public int guessedGallowTime { get; set; } = 0;
    }
}
