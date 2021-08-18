using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task WhiteBoardJoin(string UniqueId)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                var drawings = room.server.members.SelectMany(x => x.drawings).OrderBy(x => x.Uuid).ToList();
                if (drawings.Count > 0)
                {
                    //drawings.ForEach(x => x.Uuid = drawings.First().Uuid);
                    await Clients.Caller.whiteboardjoin(drawings);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task WhiteBoardUpdate(List<Drawing> updates, string UniqueId)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                room.server.members.First(x => x.ConnectionId == Context.ConnectionId).drawings.AddRange(updates);
                await Clients.GroupExcept(UniqueId, Context.ConnectionId).whiteboardupdate(updates);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task WhiteBoardClear(string UniqueId)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                room.server.members.ForEach(x => x.drawings.Clear());
                await Clients.GroupExcept(UniqueId, Context.ConnectionId).whiteboardclear(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task WhiteBoardUndo(string UniqueId, string UUID)
        {
            try
            {
                await Clients.GroupExcept(UniqueId, Context.ConnectionId).whiteboardundo(UUID);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task WhiteBoardRedo(string UniqueId, string UUID)
        {
            try
            {
                await Clients.GroupExcept(UniqueId, Context.ConnectionId).whiteboardredo(UUID);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
