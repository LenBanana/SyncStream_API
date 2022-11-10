using HtmlAgilityPack;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Games.Gallows;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Chess;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    [EnableCors("CORSPolicy")]
    public partial class ServerHub : Hub<IServerHub>
    {
        IConfiguration Configuration { get; }

        DataManager _manager;

        PostgresContext _postgres;

        GallowGameManager _gallowGameManager;

        BlackjackManager _blackjackManager;

        public ServerHub(IConfiguration configuration, DataManager manager, PostgresContext postgres, GallowGameManager gallowGameManager, BlackjackManager blackjackManager)
        {
            Configuration = configuration;
            _manager = manager;
            _postgres = postgres;
            _gallowGameManager = gallowGameManager;
            _blackjackManager = blackjackManager;
        }

#nullable enable
        public override async Task OnDisconnectedAsync(Exception? ex)
        {
            var Rooms = DataManager.GetRooms();
            int idx = Rooms.FindIndex(x => x.server.members.FirstOrDefault(y => y?.ConnectionId == Context.ConnectionId) != null);
            if (idx > -1)
            {
                Room room = Rooms[idx];
                Member? e = room.server.members.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
                e?.InvokeKick();

                var gameMode = room.GameMode;
                if (gameMode == Enums.Games.GameMode.Chess)
                {
                    var game = ChessLogic.GetChessGame(room.uniqueId);
                    if (game != null && (game.LightPlayer.ConnectionId == e?.ConnectionId || game.DarkPlayer.ConnectionId == e?.ConnectionId))
                    {
                        await EndChess(room.uniqueId);
                    }
                }
                if (gameMode == Enums.Games.GameMode.Gallows)
                {
                    var game = room.GallowGame;
                    var gameMemberIdx = game.members.FindIndex(x => x.ConnectionId == e?.ConnectionId);
                    if (gameMemberIdx > -1)
                        game.members.RemoveAt(gameMemberIdx);
                }
                if (gameMode == Enums.Games.GameMode.Blackjack)
                {
                    var blackjack = room.BlackjackGame;
                    var gameMemberIdx = blackjack.members.FindIndex(x => x.ConnectionId == e?.ConnectionId);
                    if (gameMemberIdx > -1)
                    {
                        var bjMember = blackjack.members[gameMemberIdx];
                        blackjack.members.RemoveAt(gameMemberIdx);
                        if (bjMember.waitingForBet)
                        {
                            bjMember.waitingForBet = false;
                            _blackjackManager.AskForBet(blackjack, idx + 1);
                        }
                        else if (bjMember.waitingForPull)
                        {
                            bjMember.waitingForPull = false;
                            _blackjackManager.AskForPull(blackjack, idx + 1);
                            await _blackjackManager.SendAllUsers(blackjack);
                        }
                        else
                            await _blackjackManager.SendAllUsers(blackjack);
                    }
                    if (blackjack.members.Count < 1)
                        await _blackjackManager.PlayNewRound(blackjack.RoomId);
                    else
                        await _blackjackManager.SendAllUsers(blackjack);
                }
            }
            await base.OnDisconnectedAsync(ex);
        }

        public bool CheckPrivileges(Room room, int minPriv = 1)
        {
            if (room.isPrivileged)
            {
                var member = room.server.members.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
                if (member != null)
                {
                    if (member.ishost)
                        return true;
                    var user = _postgres.Users.FirstOrDefault(x => x.username == member.username);
                    if (user == null || user.userprivileges < (UserPrivileges)minPriv)
                        return false;
                }
                else
                    return false;
            }
            return true;
        }

        public async Task SendServer(Server server, string UniqueId)
        {
            await Clients.Group(UniqueId).sendserver(server);
        }

        private Room GetRoom(string UniqueId)
        {
            List<Room> Rooms = DataManager.GetRooms();
            Room room = Rooms.FirstOrDefault(x => x.uniqueId == UniqueId);
            if (room == null)
                return null;
            return room;
        }

        public async Task GetRooms()
        {
            await Clients.Caller.getrooms(DataManager.GetRooms());
        }

        public async Task AddVideo(DreckVideo key, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "You don't have permissions to add a video to this room", Answer1 = "Ok" });
                return;
            }
            if (room.server.playlist?.Select(x => x.url).Contains(key.url) == false)
            {
                var playerType = await SendPlayerType(UniqueId, key);
                if (playerType == PlayerType.External)
                {
                    if (key.title.Length == 0)
                        key.title = "External Source";
                }
                else if (playerType == PlayerType.Vimeo)
                {
                    VimeoResponse? vimeo = Vimeo.FromUrl(key.url);
                    if (vimeo != null)
                        key.title = vimeo?.Title + (vimeo?.UserName == null ? "" : " - " + vimeo.UserName);
                }
                else if (playerType == PlayerType.YouTube)
                {
                    if (key.url.Split('?')[0].ToLower().EndsWith("playlist"))
                    {
                        await AddPlaylist(key.url, UniqueId);
                        return;
                    }
                    if (key.url.Contains("shorts"))
                        key.url = $"https://www.youtube.com/watch?v={key.url.Split('/').Last()}";
                    if (key.title == null || key.title.Length == 0)
                        key.title = await General.ResolveURL(key.url, Configuration);
                }
                else if (playerType == PlayerType.Twitch)
                {
                    var videoRegExString = @"\/videos\/(\d+)";
                    var videoRegEx = new Regex(videoRegExString);
                    var videoMatches = videoRegEx.Matches(key.url);
                    if (videoMatches != null && videoMatches.Count > 0)
                    {
                        key.url = videoMatches[0]?.Groups[1]?.Value;
                        key.title = $"Twitch Video - {key.url}";
                    }
                    else
                    {
                        var titleRegExString = @"twitch.tv\/(\w+)\/?";
                        var titleRegEx = new Regex(titleRegExString);
                        var titleMatches = titleRegEx.Matches(key.url);
                        if (titleMatches != null && titleMatches.Count > 0)
                            key.title = titleMatches[0]?.Groups[1]?.Value;
                    }
                }
                else if (playerType == PlayerType.Nothing)
                {
                    await Clients.Caller.dialog(new Dialog(AlertTypes.Warning) { Header = "Denied", Question = "Given input is not allowed", Answer1 = "Ok" });
                    return;
                }
                room.server.playlist.Add(key);
            }
            if (room.server.currentVideo.url.Length == 0 || room.server.currentVideo.ended == true)
            {
                room.server.currentVideo = key;
                await Clients.Group(UniqueId).videoupdate(key);
            }
            await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task<PlayerType> SendPlayerType(string UniqueId, DreckVideo key, PlayerType type = PlayerType.Nothing)
        {
            var result = PlayerType.Nothing;
            if (type != PlayerType.Nothing)
            {
                await Clients.Group(UniqueId).playertype(type);
                return type;
            }
            if (key.url == null || key.url.Length == 0 || key.ended)
            {
                await Clients.Group(UniqueId).playertype(PlayerType.Nothing);
                return result;
            }
            Uri uriResult;
            bool validUri = Uri.TryCreate(key.url, UriKind.Absolute, out uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
            if (!validUri)
            {
                await Clients.Group(UniqueId).playertype(PlayerType.Nothing);
                return result;
            }
            var ytRegEx = @"^(https?\:\/\/)?((www\.)?youtube\.com|youtu\.be)\/.+$";
            var twitchRegEx = @"^(https?\:\/\/)?((www\.)?twitch\.tv)\/.+$";
            var vimeoRegEx = @"^(https?\:\/\/)?((www\.)?vimeo\.com)\/.+$";
            Regex ytRegex = new Regex(ytRegEx);
            Regex twitchRegex = new Regex(twitchRegEx);
            Regex vimeoRegex = new Regex(vimeoRegEx);
            if (ytRegex.IsMatch(key.url))
                result = PlayerType.YouTube;
            else if (twitchRegex.IsMatch(key.url))
            {
                if (key.url.Contains("/clip/"))
                    return PlayerType.Nothing;                
                result = PlayerType.Twitch;
            }
            else if (vimeoRegex.IsMatch(key.url))
                result = PlayerType.Vimeo;
            else
                result = PlayerType.External;
            await Clients.Group(UniqueId).playertype(result);
            return result;
        }

        public async Task AddPlaylist(string url, string UniqueId)
        {
            try
            {
                string title, source;
                (title, source) = await General.ResolveTitle(url, 8);
                if (source.Length > 0)
                {
                    HtmlDocument doc = new HtmlDocument();
                    doc.LoadHtml(source);
                    HtmlNode vids = doc.DocumentNode.ChildNodes[1].ChildNodes[1].ChildNodes[15];
                    string json = vids.InnerText.Split(new[] { '=' }, 2)[1].Split(new[] { ';' }, 2)[0];
                    PlaylistInfo playlistInfo = new PlaylistInfo().FromJson(json);
                    var playlist = playlistInfo.Contents.TwoColumnBrowseResultsRenderer.Tabs[0].TabRenderer.Content.SectionListRenderer.Contents[0].ItemSectionRenderer.Contents[0].PlaylistVideoListRenderer.Contents;
                    foreach (var video in playlist)
                    {
                        DreckVideo vid = new DreckVideo();
                        vid.ended = false;
                        vid.title = video.PlaylistVideoRenderer?.Title.Runs[0].Text;
                        vid.url = "https://www.youtube.com/watch?v=" + video.PlaylistVideoRenderer?.VideoId;
                        await AddVideo(vid, UniqueId).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "There has been an error trying to add the playlist", Answer1 = "Ok" });
            }
        }

        public async Task RemoveVideo(string key, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "You don't have permissions to remove videos from this room", Answer1 = "Ok" });
                return;
            }
            int idx = room.server.playlist.FindIndex(x => x.url == key);
            if (idx != -1)
            {
                room.server.playlist.RemoveAt(idx);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(DataManager.GetRooms());
            }
        }

        public async Task NextVideo(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "You don't have permissions to skip videos in this room", Answer1 = "Ok" });
                return;
            }
            Server MainServer = room.server;
            if (room.server.playlist.Count > 1)
            {
                MainServer.currentVideo = room.server.playlist[1];
                room.server.playlist.RemoveAt(0);
                await SendPlayerType(UniqueId, MainServer.currentVideo);
                await Clients.Group(UniqueId).videoupdate(MainServer.currentVideo);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(DataManager.GetRooms());
                return;
            }
            else if (room.server.playlist.Count == 1)
            {
                MainServer.currentVideo.ended = true;
                room.server.playlist.RemoveAt(0);
                room.server.isplaying = false;
                await Clients.Group(UniqueId).isplayingupdate(room.server.isplaying);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.Group(UniqueId).sendserver(room.server);
                await Clients.Group(UniqueId).playertype(PlayerType.Nothing);
                await Clients.All.getrooms(DataManager.GetRooms());
            }
        }

        public async Task PlayVideo(string key, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "You don't have permissions to skip videos in this room", Answer1 = "Ok" });
                return;
            }
            Server MainServer = room.server;
            int idx = MainServer.playlist.FindIndex(x => x.url == key);
            if (idx != -1)
            {
                DreckVideo tempUrl = room.server.playlist[idx];
                room.server.playlist.RemoveAt(idx);
                room.server.playlist.RemoveAt(0);
                room.server.playlist.Insert(0, tempUrl);
                MainServer.currentVideo = room.server.playlist[0];
                await Clients.Group(UniqueId).videoupdate(MainServer.currentVideo);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(DataManager.GetRooms());
                return;
            }
        }

        public async Task MoveVideo(int fromIndex, int toIndex, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (!CheckPrivileges(room))
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "You don't have permissions to move videos in this room", Answer1 = "Ok" });
                return;
            }
            DreckVideo vid = room.server.playlist[fromIndex];
            room.server.playlist.RemoveAt(fromIndex);
            room.server.playlist.Insert(toIndex, vid);
            await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
        }

        public async Task SetTime(double time, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (!CheckPrivileges(room))
                return;
            //if (room.server.currentVideo.url.Contains("twitch.tv"))
            //    await Clients.Group(UniqueId).twitchTimeUpdate(time);
            //else
            await Clients.Group(UniqueId).timeupdate(time);
            room.server.currenttime = time;
        }

        public async Task PlayPause(bool isplaying, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (!CheckPrivileges(room))
                return;
            room.server.isplaying = isplaying;
            //if (room.server.currentVideo.url.Contains("twitch.tv"))
            //    await Clients.Group(UniqueId).twitchPlaying(isplaying);
            //else
            await Clients.Group(UniqueId).isplayingupdate(isplaying);
            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task AddRoom(Room room)
        {
            var Rooms = DataManager.GetRooms();
            int RoomCount = 0;
            while (Rooms?.Any(x => x.uniqueId == room.uniqueId) == true)
                room.uniqueId = room.uniqueId + RoomCount++;
            room.deletable = true;
            Rooms.Add(room);
            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task RemoveRoom(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            DataManager.GetRooms().Remove(room);
            await Clients.All.getrooms(DataManager.GetRooms());
        }

        public async Task Ping(DateTime date)
        {
            await Clients.Caller.PingTest(date);
        }
    }
}
