using System.Threading.Tasks;
using SyncStreamAPI.Models.GameModels.Chess;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task playchess(ChessGame game);
    Task endchess();
    Task resetchess();
    Task moveChessPiece(ChessMove move);
}