namespace SyncStreamAPI.Models.GameModels.Chess;

public class ChessMove
{
    public string Move { get; set; }
    public string FEN { get; set; }
    public string Color { get; set; }
    public bool Check { get; set; }
    public bool Checkmate { get; set; }
}