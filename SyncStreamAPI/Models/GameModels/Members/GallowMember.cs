using SyncStreamAPI.DTOModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models.GameModels.Members
{
    public class GallowMember
    {
        public GallowMember(string Username, bool IsDrawing, string connectionId)
        {
            username = Username;
            isDrawing = IsDrawing;
            ConnectionId = connectionId;
        }
        public bool ShouldSerializeConnectionId() { return false; }
        public string ConnectionId { get; set; }
        public string username { get; set; }
        public bool isDrawing { get; set; }
        public double gallowPoints { get; set; } = 0;
        public bool guessedGallow { get; set; } = false;
        public int guessedGallowTime { get; set; } = 0;
    }
}
