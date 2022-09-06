namespace SyncStreamAPI.Models.GameModels.Chess
{
    public class ChessGame
    {
        public ChessGame(Member lightPlayer, Member darkPlayer, string uniqueId)
        {
            LightPlayer = lightPlayer;
            DarkPlayer = darkPlayer;
            UniqueId = uniqueId;
            GameFEN = "";
        }

        public Member LightPlayer { get; set; }
        public Member DarkPlayer { get; set; }
        public string UniqueId { get; set; }
        public string GameFEN { get; set; }
    }
}
