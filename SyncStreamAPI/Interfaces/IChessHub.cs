﻿using SyncStreamAPI.Models.GameModels.Chess;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task playchess(ChessGame game);
        Task endchess();
        Task resetchess();
        Task moveChessPiece(ChessMove move);
    }
}
