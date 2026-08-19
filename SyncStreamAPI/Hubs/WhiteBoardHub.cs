using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
using SyncStreamAPI.ServerData.Helper;
using SyncStreamAPI.Wire;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    /// <summary>
    ///     Starts a round of gallows.
    /// </summary>
    /// <remarks>
    ///     Took only the room id and answered with the member list, while the client has always called it with
    ///     a language and a draw time as well, so SignalR refused every call for having three arguments where
    ///     one was expected. The round-starting logic it was meant to reach is <see cref="PlayGallowsSettings" />,
    ///     which the chat command path calls directly, which is why the game worked from chat and not from here.
    /// </remarks>
    public async Task PlayGallows(string UniqueId, Language language, int gameLength)
    {
        try
        {
            await PlayGallowsSettings(UniqueId, language, gameLength <= 0 ? DefaultGallowsSeconds : gameLength);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    /// <summary>Draw time used when a caller does not specify one, matching the chat command's default.</summary>
    private const int DefaultGallowsSeconds = 90;

    private async Task PlayGallowsSettings(string UniqueId, Language language, int gameLength)
    {
        try
        {
            var room = GetRoom(UniqueId);
            if (room == null) return;

            var gallows = room.GallowGame;

            if (gallows == null || gallows.PlayingGallows)
                if (_gallowGameManager.PlayNewRound(room.uniqueId))
                {
                    if (room.BlackjackGame != null) await _blackjackManager.PlayNewRound(UniqueId);

                    gallows = room.GallowGame;
                }

            if (gallows is { PlayingGallows: true })
            {
                gallows.GameLanguage = language;
                gallows.GameLength = gameLength;
                gallows.UpdateGallowWord(false);
                await Clients.Group(UniqueId).gallowusers(room.GallowGame.members.ToDtos());
                var startGallowMessage = new SystemMessage("Started a round of gallows, have fun!");
                await Clients.Group(UniqueId).sendmessage(startGallowMessage);
                await Clients.Group(UniqueId).playertype(PlayerType.WhiteBoard);
                return;
            }
            
            await RoomManager.SendPlayerType(room);

            gallows.drawings = new List<Drawing>();
            gallows.members.ForEach(x =>
            {
                x.gallowPoints = 0;
                x.guessedGallow = false;
            });
            await Clients.Group(UniqueId).playinggallows("$clearboard$");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task NewGallow(string UniqueId)
    {
        try
        {
            var room = GetRoom(UniqueId);
            if (room == null) return;

            var game = room.GallowGame;
            if (game is { PlayingGallows: true }) game.UpdateGallowWord(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task WhiteBoardJoin(string UniqueId)
    {
        try
        {
            var room = GetRoom(UniqueId);
            if (room == null) return;

            var drawings = room.GallowGame.drawings.OrderBy(x => x.Uuid).ToList();
            if (drawings.Count > 0) await Clients.Caller.whiteboardjoin(drawings);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
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
                var data = JsonConvert.SerializeObject(updates);
                drawings = JsonConvert.DeserializeObject<List<Drawing>>(data);
            }

            var room = GetRoom(UniqueId);
            if (room == null) return;

            var game = room.GallowGame;
            if (game != null) game.drawings.AddRange(drawings);

            await Clients.GroupExcept(UniqueId, Context.ConnectionId).whiteboardupdate(drawings.ToList());
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task WhiteBoardClear(string UniqueId)
    {
        try
        {
            var room = GetRoom(UniqueId);
            if (room == null) return;

            room.GallowGame.drawings.Clear();
            await Clients.GroupExcept(UniqueId, Context.ConnectionId).whiteboardclear(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
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
            Console.WriteLine(ex.ToString());
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
            Console.WriteLine(ex.ToString());
        }
    }
}