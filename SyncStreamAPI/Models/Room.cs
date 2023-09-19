using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Games.Gallows;
using SyncStreamAPI.Models.GameModels.Blackjack;
using SyncStreamAPI.Models.GameModels.Chess;
using SyncStreamAPI.Models.GameModels.Gallows;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace SyncStreamAPI.Models
{
    public class Room
    {
        public string uniqueId { get; set; }
        public string name { get; set; }
        public string password { get; set; }
        public bool isPrivileged { get; set; }
        public bool deletable { get; set; } = true;
        [NotMapped]
        public string? CurrentStreamer { get; set; }
        [NotMapped]
        public Server server { get { return _server; } set { _server = value; } }
        [NotMapped]
        private Server _server { get; set; } = new Server();
        [NotMapped]
        public GameMode GameMode => GetGameMode();
        [NotMapped]
        public GallowLogic GallowGame { get { var gG = GallowGameManager.gallowGames.FirstOrDefault(x => x != null && x.RoomId == uniqueId); return gG; } }
        [NotMapped]
        public BlackjackLogic BlackjackGame { get { var bG = BlackjackManager.blackjackGames.FirstOrDefault(x => x != null && x.RoomId == uniqueId); return bG; } }

        public Room(string Name, string UnqiueId, bool Deletable, bool Privileged)
        {
            name = Name;
            uniqueId = UnqiueId;
            deletable = Deletable;
            isPrivileged = Privileged;
        }

        GameMode GetGameMode()
        {
            var gM = GallowGame;
            var bG = BlackjackGame;
            if (gM == null && bG == null && ChessLogic.GetChessGame(uniqueId) != null)
            {
                return GameMode.Chess;
            }

            return gM == null ? bG == null ? GameMode.NotPlaying : GameMode.Blackjack : GameMode.Gallows;
        }

    }
}
