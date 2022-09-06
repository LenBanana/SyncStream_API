using System.Collections.Generic;
using System.Linq;

namespace SyncStreamAPI.Models.GameModels.Chess
{
#nullable enable
    public class ChessLogic
    {
        static List<ChessGame> ChessGames { get; set; } = new List<ChessGame>();

        public static ChessGame? GetChessGame(string uniqueId)
        {
            return ChessGames.FirstOrDefault(x => x.UniqueId == uniqueId);
        }

        public static ChessGame AddChessGame(string uniqueId, Member lightPlayer, Member darkPlayer)
        {
            var game = new ChessGame(lightPlayer, darkPlayer, uniqueId);
            ChessGames.Add(game);
            return game;
        }

        public static void RemoveChessGame(string uniqueId)
        {
            var idx = ChessGames.FindIndex(x => x.UniqueId == uniqueId);
            if (idx == -1)
                return;
            ChessGames.RemoveAt(idx);
        }
    }
}
