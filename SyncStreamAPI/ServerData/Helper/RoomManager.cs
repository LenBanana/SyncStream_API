using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.Bots;
using YoutubeDLSharp.Options;
using SyncStreamAPI.Wire;

namespace SyncStreamAPI.ServerData.Helper;

public class RoomManager
{
    public RoomManager(IServiceProvider serviceProvider, ConcurrentDictionary<string, Room> rooms)
    {
        ServiceProvider = serviceProvider;
        Rooms = rooms;
    }

    private static IServiceProvider ServiceProvider { get; set; }

    /// <summary>
    ///     Live rooms, keyed by their id.
    /// </summary>
    /// <remarks>
    ///     Was a <see cref="System.Collections.Concurrent.BlockingCollection{T}" />, which is a queue: looking
    ///     a room up meant copying the whole collection and scanning it, and removing one meant draining every
    ///     room out under a lock and putting the survivors back. A keyed dictionary is what the code always
    ///     wanted, and removal stops being a operation that briefly empties the server.
    /// </remarks>
    private static ConcurrentDictionary<string, Room> Rooms { get; set; } = new();

    public static Room? GetRoom(string UniqueId)
    {
        if (string.IsNullOrEmpty(UniqueId)) return null;
        return Rooms.TryGetValue(UniqueId, out var room) ? room : null;
    }

    /// <summary>Adds a room, or returns false when the id is already taken.</summary>
    public static bool AddRoom(Room room)
    {
        return room.uniqueId != null && Rooms.TryAdd(room.uniqueId, room);
    }

    public static bool RemoveRoom(string uniqueId)
    {
        return Rooms.TryRemove(uniqueId, out _);
    }

    public static IEnumerable<Room> GetRooms()
    {
        // Ordered by creation, so the lobby lists rooms the way they were made and a new one appears at the
        // end. A dictionary returns its values in hash order, which is no order at all.
        return Rooms.Values.OrderBy(room => room.Sequence).ToList();
    }


    public static async Task<string> AddVideo(DreckVideo key, string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return "Room not found";

        using var scope = ServiceProvider.CreateScope();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var vidEnded = room.server.currentVideo.url.Length == 0 || room.server.currentVideo.ended;
        if (room.server.playlist?.Select(x => x.url).Contains(key.url) == false)
        {
            room.server.currentVideo = key;
            var playerType = await SendPlayerType(room, vidEnded);
            switch (playerType)
            {
                case PlayerType.Live:
                {
                    if (key.title.Length == 0) key.title = key.url.Split('=').Last().FirstCharToUpper();

                    break;
                }
                case PlayerType.External:
                {
                    if (key.title.Length == 0) key.title = "External Source";

                    break;
                }
                case PlayerType.Vimeo:
                {
                    var vimeo = Vimeo.FromUrl(key.url);
                    if (vimeo != null)
                        key.title = vimeo?.Title + (vimeo?.UserName == null ? "" : " - " + vimeo.UserName);

                    break;
                }
                case PlayerType.YouTube when key.url.Split('?')[0].ToLower().EndsWith("playlist"):
                    await AddPlaylist(key.url, UniqueId);
                    return "";
                case PlayerType.YouTube:
                {
                    if (key.url.Contains("shorts"))
                        key.url = $"https://www.youtube.com/watch?v={key.url.Split('/').Last()}";

                    if (string.IsNullOrEmpty(key.title)) key.title = await General.ResolveUrl(key.url, config);

                    break;
                }
                case PlayerType.Twitch:
                {
                    var urlPatterns = new (string pattern, string titlePrefix)[]
                    {
                        (@"\/videos\/(\d+)", "Twitch Video - "),
                        (@"clips\.twitch\.tv\/([\w\-]+)", "Twitch Clip - ")
                    };

                    foreach (var (pattern, titlePrefix) in urlPatterns)
                    {
                        var regex = new Regex(pattern);
                        var matches = regex.Matches(key.url);

                        if (matches.Count <= 0) continue;
                        key.url = key.url;
                        key.title = $"{titlePrefix}{key.url}";
                        break;
                    }

                    if (string.IsNullOrEmpty(key.title))
                    {
                        const string titleRegExString = @"twitch.tv\/(\w+)\/?";
                        var titleRegEx = new Regex(titleRegExString);
                        var titleMatches = titleRegEx.Matches(key.url);
                        if (titleMatches.Count > 0) key.title = titleMatches[0]?.Groups[1]?.Value;
                    }

                    break;
                }
                case PlayerType.Nothing:
                    return "The video you tried to add is not supported";
            }

            room.server.playlist.Add(key);
        }

        if (vidEnded)
        {
            room.server.currentVideo = key;
            await hub.Clients.Group(UniqueId).videoupdate(key);
        }

        await hub.Clients.Group(UniqueId).playlistupdate(room.server.playlist);
        await hub.Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
        return "";
    }


    public static async Task<PlayerType> SendPlayerType(Room room, bool sendToUsers = true,
        PlayerType type = PlayerType.Nothing)
    {
        using var scope = ServiceProvider.CreateScope();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();

        switch (room.GameMode)
        {
            case GameMode.Blackjack:
                if (sendToUsers) await hub.Clients.Group(room.uniqueId).playertype(PlayerType.Blackjack);
                return PlayerType.Blackjack;
            case GameMode.Chess:
                if (sendToUsers) await hub.Clients.Group(room.uniqueId).playertype(PlayerType.Chess);
                return PlayerType.Chess;
            case GameMode.Gallows:
                if (sendToUsers) await hub.Clients.Group(room.uniqueId).playertype(PlayerType.WhiteBoard);
                return PlayerType.WhiteBoard;
        }
        
        var uniqueId = room.uniqueId;
        var key = room.server.currentVideo;
        var result = PlayerType.Nothing;

        // A file share takes precedence over everything except active game modes (handled above).
        if (room.IsFileSharingActive)
        {
            if (sendToUsers) await hub.Clients.Group(uniqueId).playertype(PlayerType.FileShare);
            return PlayerType.FileShare;
        }

        if (room.CurrentStreamer != null)
        {
            if (sendToUsers) await hub.Clients.Group(uniqueId).playertype(PlayerType.WebRtc);

            return PlayerType.WebRtc;
        }

        if (type != PlayerType.Nothing)
        {
            if (sendToUsers) await hub.Clients.Group(uniqueId).playertype(type);

            return type;
        }

        if (string.IsNullOrEmpty(key.url) || key.ended)
        {
            if (sendToUsers) await hub.Clients.Group(uniqueId).playertype(PlayerType.Nothing);

            return result;
        }

        var validUri = Uri.TryCreate(key.url, UriKind.Absolute, out var uriResult) &&
                       (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        if (!validUri)
        {
            if (sendToUsers) await hub.Clients.Group(uniqueId).playertype(PlayerType.Nothing);

            return result;
        }

        const string ytRegEx = @"^(https?\:\/\/)?((www\.)?youtube\.com|youtu\.be)\/.+$";
        const string twitchRegEx = @"^(https?\:\/\/)?((www\.)?twitch\.tv|clips\.twitch\.tv)\/.+$";
        const string vimeoRegEx = @"^(https?\:\/\/)?((www\.)?vimeo\.com)\/.+$";
        var ytRegex = new Regex(ytRegEx);
        var twitchRegex = new Regex(twitchRegEx);
        var vimeoRegex = new Regex(vimeoRegEx);
        if (key.url.ToLower().Contains("//live.drecktu.be/") || key.url.StartsWith("rtmp") ||
            key.url.Contains("//drecktu.be:8088/live"))
        {
            if (sendToUsers)
                await hub.Clients.Group(General.BottedInGroupName).sendBotChannelUpdate(new BotLiveChannelInfo
                    { ChannelId = key.url.Split('=').Last(), RoomName = uniqueId });

            result = PlayerType.Live;
        }
        else if (ytRegex.IsMatch(key.url))
        {
            result = PlayerType.YouTube;
        }
        else if (twitchRegex.IsMatch(key.url))
        {
            result = PlayerType.Twitch;
        }
        else if (vimeoRegex.IsMatch(key.url))
        {
            result = PlayerType.Vimeo;
        }
        else
        {
            result = PlayerType.External;
        }

        if (sendToUsers) await hub.Clients.Group(uniqueId).playertype(result);

        return result;
    }


    public static async Task AddPlaylist(string url, string UniqueId)
    {
        try
        {
            var ytdl = General.GetYoutubeDl();
            var playlistInfo = await ytdl.RunVideoDataFetch(url, overrideOptions: new OptionSet
            {
                ForceIPv4 = true
            });
            if (playlistInfo != null)
            {
                var vids = playlistInfo.Data.Entries.Select(x => new DreckVideo(x.Title, x.Url, false,
                    TimeSpan.FromSeconds(x.Duration ?? 0d), "Playlist")).ToList();
                vids.ForEach(async x => { await AddVideo(x, UniqueId); });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}