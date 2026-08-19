using System.Threading.Tasks;
using SyncStreamAPI.Models.GameModels.Chess;
using SyncStreamAPI.Wire.Games;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task playchess(ChessGameDto game);
    Task endchess();
    Task resetchess();
    Task moveChessPiece(ChessMove move);
}