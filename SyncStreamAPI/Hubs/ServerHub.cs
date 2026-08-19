using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Games.Gallows;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Chess;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Helper.Streaming;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Helper;
using SyncStreamAPI.Wire;
using System.Text.RegularExpressions;
using SyncStreamAPI.Wire.Rooms;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SyncStreamAPI.Hubs;

[EnableCors("CORSPolicy")]
public partial class ServerHub : Hub<IServerHub>
{
    private readonly BlackjackManager _blackjackManager;
    private readonly GallowGameManager _gallowGameManager;

    private readonly MainManager _manager;

    private readonly WebRtcSfuManager _webRtcSfuManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PostgresContext _postgres;
    private readonly IRoomStreamService _roomStreamService;

    public ServerHub(IConfiguration configuration, MainManager manager, PostgresContext postgres,
        GallowGameManager gallowGameManager, BlackjackManager blackjackManager,
        WebRtcSfuManager webRtcSfuManager, RtmpFileShareManager rtmpFileShareManager,
        IRoomStreamService roomStreamService, IHttpContextAccessor httpContextAccessor)
    {
        Configuration = configuration;
        _manager = manager;
        _webRtcSfuManager = webRtcSfuManager;
        _httpContextAccessor = httpContextAccessor;
        _rtmpFileShareManager = rtmpFileShareManager;
        _roomStreamService = roomStreamService;
        _postgres = postgres;
        _gallowGameManager = gallowGameManager;
        _blackjackManager = blackjackManager;
    }

    private static readonly Regex RoomIdRegex = new(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

    private IConfiguration Configuration { get; }

    /// <summary>
    ///     Puts the connection into the channels its identity entitles it to.
    /// </summary>
    /// <remarks>
    ///     This used to happen inside LoginRequest, which meant a reconnect kept its room but silently lost
    ///     every per-user channel until the client logged in again. Deriving it from the connection's own
    ///     identity makes it survive reconnects for free.
    /// </remarks>
    public override async Task OnConnectedAsync()
    {
        var caller = Context.GetCaller();
        if (!caller.IsGuest)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, caller.UserId!.Value.ToString());
            await Groups.AddToGroupAsync(Context.ConnectionId, General.LoggedInGroupName);
            if (caller.HasAtLeast(UserPrivileges.Elevated))
                await Groups.AddToGroupAsync(Context.ConnectionId, General.AdminGroupName);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        var rooms = MainManager.GetRooms();
        var room = rooms.FirstOrDefault(x =>
            x.server.members.FirstOrDefault(y => y?.ConnectionId == Context.ConnectionId) != null);
        if (room != null)
        {
            var e = room.server.members.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
            e?.InvokeKick();

            // If this connection owned an active WebRTC file share, stop it cleanly.
            if (room.IsFileSharingActive && room.FileShareInitiator == Context.ConnectionId)
                await StopFileShareInternalAsync(room);

            // If this connection owned an active RTMP file share, stop it cleanly.
            if (room.IsRtmpFileShareActive && room.RtmpFileShareInitiator == Context.ConnectionId)
                await StopRtmpFileShareInternalAsync(room);

            if (room.CurrentStreamer == Context.ConnectionId)
            {
                room.CurrentStreamer = null;
                // stopWebRtcStream sent below via StopWebRtcStreamOnDisconnect
            }

            var gameMode = room.GameMode;
            switch (gameMode)
            {
                case GameMode.Chess:
                {
                    var game = ChessLogic.GetChessGame(room.uniqueId);
                    if (game != null && (game.LightPlayer.ConnectionId == e?.ConnectionId ||
                                         game.DarkPlayer.ConnectionId == e?.ConnectionId))
                        await EndChess(room.uniqueId);

                    break;
                }
                case GameMode.Gallows:
                {
                    var game = room.GallowGame;
                    var gameMemberIdx = game.members.FindIndex(x => x.ConnectionId == e?.ConnectionId);
                    if (gameMemberIdx > -1) game.members.RemoveAt(gameMemberIdx);

                    break;
                }
                case GameMode.Blackjack:
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
                            _blackjackManager.AskForBet(blackjack, gameMemberIdx + 1);
                        }
                        else if (bjMember.waitingForPull)
                        {
                            bjMember.waitingForPull = false;
                            _blackjackManager.AskForPull(blackjack, gameMemberIdx + 1);
                            await _blackjackManager.SendAllUsers(blackjack);
                        }
                        else
                        {
                            await _blackjackManager.SendAllUsers(blackjack);
                        }
                    }

                    if (blackjack.members.Count < 1)
                        await _blackjackManager.PlayNewRound(blackjack.RoomId);
                    else
                        await _blackjackManager.SendAllUsers(blackjack);

                    break;
                }
                case GameMode.NotPlaying:
                    break;
            }
        }

        // Clean up VoIP room membership if the connection was in an audio room.
        if (_voipRoomByConnection.TryRemove(Context.ConnectionId, out var voipRoomId))
            await LeaveAudioRoomInternal(Context.ConnectionId, voipRoomId);

        // Clean up active P2P streaming (Discord-style; handles member list update + stopWebRtcStream).
        await StopWebRtcStreamOnDisconnect(Context.ConnectionId);

        // Clean up SFU room membership and release server-side resources.
        await OnSfuDisconnectedAsync(Context.ConnectionId);

        await base.OnDisconnectedAsync(ex);
    }

    /// <summary>The authenticated account's id, or null for a guest.</summary>
    private int? CallerUserId => Context.GetCaller().UserId;

    /// <summary>
    ///     Loads the calling account, or null for a guest.
    /// </summary>
    /// <remarks>
    ///     Replaces the pattern this codebase repeated 33 times, looking a user up by a remember token that
    ///     arrived as a method argument.
    /// </remarks>
    private Task<DbUser?> GetCallerUserAsync()
    {
        var userId = CallerUserId;
        if (userId == null) return Task.FromResult<DbUser?>(null);
        return _postgres.Users.FirstOrDefaultAsync(candidate => candidate.ID == userId);
    }

    /// <summary>
    ///     Whether the caller may perform a privileged action in this room.
    /// </summary>
    /// <remarks>
    ///     Reads the connection's own claims. It used to look the member's <c>username</c> up in the database
    ///     and take that account's privileges, and the username was whatever the client passed to AddUser, so
    ///     joining a privileged room under someone else's name borrowed their privileges.
    /// </remarks>
    private bool CheckPrivileges(Room room, UserPrivileges minimum = UserPrivileges.Approved)
    {
        if (!room.isPrivileged) return true;

        var member = room.server.members.FirstOrDefault(x => x?.ConnectionId == Context.ConnectionId);
        if (member == null) return false;
        if (member.ishost) return true;

        return Context.GetCaller().HasAtLeast(minimum);
    }

    private Room? GetRoom(string UniqueId)
    {
        var rooms = MainManager.GetRooms();
        var room = rooms.FirstOrDefault(x => x.uniqueId == UniqueId);
        return room ?? null;
    }

    public async Task GetRooms()
    {
        await Clients.Caller.getrooms(MainManager.GetRooms().ToSummaries());
    }

    public async Task AddVideo(DreckVideo key, string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        if (!CheckPrivileges(room))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header = "Error", Question = "You don't have permissions to add a video to this room",
                Answer1 = "Ok"
            });
            return;
        }

        var result = await RoomManager.AddVideo(key, UniqueId);
        if (!string.IsNullOrEmpty(result))
            await Clients.Caller.dialog(new Dialog(AlertType.Warning)
                { Header = "Denied", Question = result, Answer1 = "Ok" });
    }

    public async Task RemoveVideo(string key, string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        if (!CheckPrivileges(room))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header = "Error", Question = "You don't have permissions to remove videos from this room",
                Answer1 = "Ok"
            });
            return;
        }

        var idx = room.server.playlist.FindIndex(x => x.url == key);
        if (idx != -1)
        {
            room.server.playlist.RemoveAt(idx);
            await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
            await Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
        }
    }

    public async Task NextVideo(string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        if (!CheckPrivileges(room))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header = "Error", Question = "You don't have permissions to skip videos in this room",
                Answer1 = "Ok"
            });
            return;
        }

        // Skipping while a file share is active stops the share first, then proceeds.
        if (room.IsFileSharingActive)
            await StopFileShareInternalAsync(room);

        var mainServer = room.server;
        switch (room.server.playlist.Count)
        {
            case > 1:
                mainServer.currentVideo = room.server.playlist[1];
                room.server.playlist.RemoveAt(0);
                await RoomManager.SendPlayerType(room);
                await Clients.Group(UniqueId).videoupdate(mainServer.currentVideo);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
                return;
            case 1:
                mainServer.currentVideo.ended = true;
                room.server.playlist.RemoveAt(0);
                room.server.isplaying = false;
                await Clients.Group(UniqueId).isplayingupdate(room.server.isplaying);
                await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
                await Clients.Group(UniqueId).playertype(PlayerType.Nothing);
                await Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
                break;
        }
    }

    public async Task PlayVideo(string key, string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        if (!CheckPrivileges(room))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header = "Error", Question = "You don't have permissions to skip videos in this room",
                Answer1 = "Ok"
            });
            return;
        }

        var mainServer = room.server;
        var idx = mainServer.playlist.FindIndex(x => x.url == key);
        if (idx != -1)
        {
            var tempUrl = room.server.playlist[idx];
            room.server.playlist.RemoveAt(idx);
            room.server.playlist.RemoveAt(0);
            room.server.playlist.Insert(0, tempUrl);
            mainServer.currentVideo = room.server.playlist[0];
            await Clients.Group(UniqueId).videoupdate(mainServer.currentVideo);
            await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
            await Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
        }
    }

    public async Task MoveVideo(int fromIndex, int toIndex, string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        if (!CheckPrivileges(room))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header = "Error", Question = "You don't have permissions to move videos in this room",
                Answer1 = "Ok"
            });
            return;
        }

        var vid = room.server.playlist[fromIndex];
        room.server.playlist.RemoveAt(fromIndex);
        room.server.playlist.Insert(toIndex, vid);
        await Clients.Group(UniqueId).playlistupdate(room.server.playlist);
    }

    public async Task SetTime(double time, string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        if (!CheckPrivileges(room)) return;

        await Clients.Group(UniqueId).timeupdate(time);
        room.server.currenttime = time;
    }

    public async Task PlayPause(bool isplaying, string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        if (!CheckPrivileges(room)) return;

        room.server.isplaying = isplaying;
        await Clients.Group(UniqueId).isplayingupdate(isplaying);
        await Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
    }

    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task AddRoom(CreateRoomRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request?.name) || string.IsNullOrWhiteSpace(request.uniqueId))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "A room needs a name", Answer1 = "Ok" });
            return;
        }

        if (request.name.Length > General.MaxRoomNameLength)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header = "Error",
                Question = $"Room names are limited to {General.MaxRoomNameLength} characters",
                Answer1 = "Ok"
            });
            return;
        }

        // The id becomes a SignalR group name and a lookup key, so keep it to a known-safe shape.
        if (!RoomIdRegex.IsMatch(request.uniqueId))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Invalid room id", Answer1 = "Ok" });
            return;
        }

        // Claim an id by adding, rather than checking first and adding after: two callers creating a room
        // at the same moment both passed a look-then-add check, and one of the rooms then vanished.
        Room? room = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var uniqueId = attempt == 0 ? request.uniqueId : request.uniqueId + attempt;

            // Constructed here, not bound: the live server state is the server's to own.
            var candidate = new Room(request.name, uniqueId, request.deletable, false)
            {
                password = request.password
            };

            if (!RoomManager.AddRoom(candidate)) continue;

            room = candidate;
            break;
        }

        if (room == null)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Could not find a free room id", Answer1 = "Ok" });
            return;
        }
        if (!room.deletable)
        {
            var dbRoom = new DbRoom(room);
            await _postgres.Rooms.AddAsync(dbRoom);
            await _postgres.SaveChangesAsync();
        }

        await Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
    }

    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task RemoveRoom(string uniqueId)
    {
        var room = GetRoom(uniqueId);
        if (room == null || room.isPrivileged) return;

        if (RoomManager.RemoveRoom(uniqueId))
        {
            var dbRoom = _postgres.Rooms.FirstOrDefault(x => x.uniqueId == uniqueId);
            if (dbRoom != null)
            {
                _postgres.Rooms.Remove(dbRoom);
                await _postgres.SaveChangesAsync();
            }

            await Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
        }
        else
        {
            var errorMessage = $"There was an error attempting to remove {room.name}";
            await Clients.Caller.dialog(new Dialog(AlertType.Danger, errorMessage));
        }
    }

    public async Task Ping(DateTime date)
    {
        await Clients.Caller.PingTest(date);
    }
}