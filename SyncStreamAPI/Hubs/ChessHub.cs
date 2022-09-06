using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Gallows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task PlayChess(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            if (MainServer.members.Count > 2)
                return;
            await Clients.Group(UniqueId).playchess();
        }

        public async Task MoveChessPiece(string UniqueId, string move)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            await Clients.GroupExcept(UniqueId, Context.ConnectionId).moveChessPiece(move);
        }
    }
}
