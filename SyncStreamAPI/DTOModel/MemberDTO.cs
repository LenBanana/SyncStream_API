using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.DTOModel
{
    public class MemberDTO
    {
        public string username { get; set; }
        public bool ishost { get; set; }
        public double gallowPoints { get; set; } = 0;
        public bool guessedGallow { get; set; } = false;

        public MemberDTO(string Username, bool IsHost, double GallowPoints, bool GuessedGallow)
        {
            username = Username;
            ishost = IsHost;
            gallowPoints = GallowPoints;
            guessedGallow = GuessedGallow;
        }

    }
}
