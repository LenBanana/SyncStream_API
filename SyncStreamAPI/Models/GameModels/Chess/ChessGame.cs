namespace SyncStreamAPI.Models.GameModels.Chess
{
    public class ChessGame
    {
        public ChessGame(Member lightPlayer, Member darkPlayer, string uniqueId, bool lightPlayerAi = false, bool darkPlayerAi = false)
        {
            LightPlayer = lightPlayer;
            DarkPlayer = darkPlayer;
            UniqueId = uniqueId;
            GameFEN = "";
            DarkPlayerAi = darkPlayerAi;
            LightPlayerAi = lightPlayerAi;
        }

        public Member LightPlayer { get; set; }
        public Member DarkPlayer { get; set; }
        public bool DarkPlayerAi { get; set; }
        public bool LightPlayerAi { get; set; }
        public string UniqueId { get; set; }
        public string GameFEN { get; set; }
    }
}
