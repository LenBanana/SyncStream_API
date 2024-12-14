using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Chess;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    public async Task PlayChess(string UniqueId, string user1 = "", string user2 = "", bool lightPlayerAi = false,
        bool darkPlayerAi = false)
    {
        await Clients.Group(UniqueId).playertype(PlayerType.Chess);
        var room = GetRoom(UniqueId);
        if (room == null) return;

        var MainServer = room.server;
        if (MainServer.members.Count < 2) return;

        var member1 = MainServer.members.FirstOrDefault(x => x.username == user1);
        var member2 = MainServer.members.FirstOrDefault(x => x.username == user2);
        var lightPlayer = member1 != null ? member1 : MainServer.members[0];
        var darkPlayer = member2 != null ? member2 : MainServer.members[1];
        var game = ChessLogic.AddChessGame(UniqueId, lightPlayer, darkPlayer, lightPlayerAi, darkPlayerAi);
        await Clients.Client(lightPlayer.ConnectionId)
            .sendmessage(new SystemMessage("You are now chess player light!"));
        await Clients.Client(darkPlayer.ConnectionId).sendmessage(new SystemMessage("You are now chess player dark!"));
        await Clients.Group(UniqueId).playchess(game);
    }

    public async Task EndChess(string UniqueId)
    {
        ChessLogic.RemoveChessGame(UniqueId);
        await Clients.Group(UniqueId).endchess();
    }

    public async Task ResetChess(string UniqueId)
    {
        await Clients.GroupExcept(UniqueId, Context.ConnectionId).resetchess();
    }

    public async Task MoveChessPiece(string UniqueId, ChessMove move)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        var MainServer = room.server;
        var game = ChessLogic.GetChessGame(UniqueId);
        if (game == null) return;

        if (move.Check)
            await Clients.Group(UniqueId).sendmessage(new SystemMessage($"Player {move.Color} made a check!"));

        if (move.Checkmate)
            await Clients.Group(UniqueId).sendmessage(new SystemMessage($"Player {move.Color} made a checkmate!"));

        game.GameFEN = move.FEN;
        await Clients.GroupExcept(UniqueId, Context.ConnectionId).moveChessPiece(move);
    }
}