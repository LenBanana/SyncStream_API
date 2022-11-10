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
        public async Task PlayGallows(string UniqueId)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                await Clients.Group(UniqueId).gallowusers(room.GallowGame.members);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        public async Task PlayGallowsSettings(string UniqueId, Language language, int gameLength)
        {
            try
            {
                await Clients.Group(UniqueId).playertype(PlayerType.WhiteBoard);
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                var gallows = room.GallowGame;

                if (gallows == null || gallows.PlayingGallows)
                {
                    if (_gallowGameManager.PlayNewRound(room.uniqueId))
                    {
                        if (room.BlackjackGame != null)
                            await _blackjackManager.PlayNewRound(UniqueId);
                        gallows = room.GallowGame;
                    }
                }
                if (gallows != null && gallows.PlayingGallows)
                {
                    gallows.GameLanguage = language;
                    gallows.GameLength = gameLength;
                    gallows.UpdateGallowWord(false);
                    await Clients.Group(UniqueId).gallowusers(room.GallowGame.members);
                    var startGallowMessage = new SystemMessage($"Started a round of gallows, have fun!");
                    await Clients.Group(UniqueId).sendmessage(startGallowMessage);
                    return;
                }
                gallows.drawings = new List<Drawing>();
                gallows.members.ForEach(x => { x.gallowPoints = 0; x.guessedGallow = false; });
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
                var game = room.GallowGame;
                if (game != null && game.PlayingGallows)
                    game.UpdateGallowWord(false);
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
                var drawings = room.GallowGame.drawings.OrderBy(x => x.Uuid).ToList();
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
                var game = room.GallowGame;
                if (game != null)
                    game.drawings.AddRange(drawings);

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
                room.GallowGame.drawings.Clear();
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
