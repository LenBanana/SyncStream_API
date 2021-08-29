using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Games.Gallows;
using SyncStreamAPI.Models.GameModels.Blackjack;
using SyncStreamAPI.Models.GameModels.Gallows;
using System.Linq;

namespace SyncStreamAPI.Models
{
    public class Room
    {
        public string uniqueId { get; set; }
        public string name { get; set; }
        public string password { get; set; }
        public bool deletable { get; set; } = true;
        public Server server { get { return _server; } set { _server = value; } }
        private Server _server { get; set; } = new Server();
        public GameMode GameMode { get { var gM = GallowGame; var bG = BlackjackGame; return gM == null ? bG == null ? GameMode.NotPlaying : GameMode.Blackjack : GameMode.Gallows; } }
        public GallowLogic GallowGame { get { var gG = GallowGameManager.gallowGames.FirstOrDefault(x => x != null && x.RoomId == uniqueId); return gG; } }
        public BlackjackLogic BlackjackGame { get { var bG = BlackjackManager.blackjackGames.FirstOrDefault(x => x != null && x.RoomId == uniqueId); return bG; } }

        public Room(string Name, string UnqiueId, bool Deletable)
        {
            name = Name;
            uniqueId = UnqiueId;
            deletable = Deletable;
        }

    }
}
