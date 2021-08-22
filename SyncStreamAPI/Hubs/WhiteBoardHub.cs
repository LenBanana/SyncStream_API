using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task PlayGallows(string UniqueId, Language language)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;

                room.server.PlayingGallows = !room.server.PlayingGallows;
                room.server.GameLanguage = language;
                if (room.server.PlayingGallows)
                {
                    room.server.UpdateGallowWord(false);
                    await Clients.Group(UniqueId).gallowusers(room.server.members.Select(x => x.ToDTO()).ToList());
                    return;
                }
                room.server.members.ForEach(x => { x.gallowPoints = 0; x.guessedGallow = false; x.drawings = new List<Drawing>(); });
                await Clients.Group(UniqueId).playinggallows("$clearboard$");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task NewGallow(string UniqueId)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                if (room.server.PlayingGallows)
                    room.server.UpdateGallowWord(false);                
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

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
                    await Clients.Caller.whiteboardjoin(drawings);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task WhiteBoardUpdate(List<object> updates, string UniqueId)
        {
            try
            {
                IList<Drawing> drawings = new List<Drawing>();
                try
                {
                    drawings = (IList<Drawing>)updates;
                }
                catch
                {
                    var data = Newtonsoft.Json.JsonConvert.SerializeObject(updates);
                    drawings = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Drawing>>(data);
                }
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                room.server.members.First(x => x.ConnectionId == Context.ConnectionId).drawings.AddRange(drawings);
                await Clients.GroupExcept(UniqueId, Context.ConnectionId).whiteboardupdate(drawings.ToList());
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
