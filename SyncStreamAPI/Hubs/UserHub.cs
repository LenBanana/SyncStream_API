using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Chess;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Helper;
using SyncStreamAPI.Wire;
using SyncStreamAPI.Wire.Games;
using SyncStreamAPI.Auth;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    /// <summary>
    ///     Joins a room.
    /// </summary>
    /// <param name="displayName">
    ///     The name a guest would like to be called. Ignored for signed-in accounts, which always appear
    ///     under their account name.
    /// </param>
    /// <remarks>
    ///     This used to take the username to join as, and room privileges were then resolved by looking that
    ///     name up in the database, so joining as someone else borrowed their privileges. The identity now
    ///     comes from the connection and only a guest's chosen label is negotiable.
    /// </remarks>
    /// <param name="password">The room password as typed, or null for an open room.</param>
    [ErrorHandling]
    public async Task AddUser(string displayName, string UniqueId, string password)
    {
        var ip = Context.ConnectionId;
        var room = GetRoom(UniqueId);
        if (room == null)
        {
            await Clients.Caller.adduserupdate((int)UserUpdate.RoomNotExist);
            return;
        }

        var mainServer = room.server;
        var caller = Context.GetCaller();

        var admission = RoomAdmission.Evaluate(
            mainServer,
            caller,
            ip,
            displayName,
            password,
            room.password,
            candidate => _postgres.Users.Any(user => user.username == candidate));

        switch (admission.Outcome)
        {
            case AdmissionOutcome.WrongPassword:
                await Clients.Caller.adduserupdate((int)UserUpdate.WrongPassword);
                return;

            case AdmissionOutcome.Banned:
                await Clients.Caller.adduserupdate((int)UserUpdate.Banned);
                return;

            case AdmissionOutcome.NameNotAllowed:
                await Clients.Caller.adduserupdate((int)UserUpdate.NameNotAllowed);
                return;

            case AdmissionOutcome.AlreadyPresent:
                // Joining twice on one connection is a repeated request, not an error.
                await Clients.Caller.adduserupdate((int)UserUpdate.Success);
                return;

            case AdmissionOutcome.Reclaimed:
            {
                // A reconnecting account keeps its name, host role and place in the turn order. Its previous
                // connection is already gone, or about to be, and the old entry would otherwise linger as a
                // ghost in everyone's member list.
                var existing = admission.Existing!;
                // Whatever the old connection was broadcasting, sharing or saying in the call ended with it;
                // the account does not carry it over to the new connection.
                if (existing.ConnectionId != null && existing.ConnectionId != ip)
                    await ReleaseMediaAsync(room, existing.ConnectionId);
                existing.IsStreaming = false;
                existing.ConnectionId = ip;
                await Groups.AddToGroupAsync(ip, UniqueId);
                await Clients.Group(UniqueId).userupdate(mainServer.members.Select(x => x?.ToDTO()).ToList());
                await SendRoomStateToCaller(room, existing);
                return;
            }
        }

        var newMember = new Member
        {
            Identity = caller.Key,
            username = admission.ResolvedName,
            ConnectionId = ip,
            RoomId = UniqueId
        };

        await Groups.AddToGroupAsync(Context.ConnectionId, UniqueId);
        mainServer.Join(newMember);

        await Clients.Group(UniqueId).userupdate(mainServer.members?.Select(x => x?.ToDTO()).ToList());
        await SendRoomStateToCaller(room, newMember);
    }

    /// <summary>
    ///     Brings one caller up to date with everything happening in a room.
    /// </summary>
    /// <remarks>
    ///     Shared by a first join and by a reconnecting member reclaiming their seat. The reclaim path used to
    ///     return early, so somebody who dropped and came back rejoined the room and then sat looking at a
    ///     blank or stale player: no player type, no current video, no position, no playlist, and no notice of
    ///     an active file share.
    /// </remarks>
    private async Task SendRoomStateToCaller(Room room, Member member)
    {
        var mainServer = room.server;

        switch (room.GameMode)
        {
            case GameMode.Chess:
            {
                var game = ChessLogic.GetChessGame(room.uniqueId);
                if (game != null) await Clients.Caller.playchess(new ChessGameDto(game));

                break;
            }
            case GameMode.Gallows:
            {
                var game = room.GallowGame;
                if (game.members.FindIndex(x => x.ConnectionId == member.ConnectionId) == -1)
                {
                    game.AddMember(member);
                    await Clients.Caller.playinggallows(game.GallowWord);
                    await Clients.Caller.gallowusers(game.members.ToDtos());
                }

                break;
            }
            case GameMode.Blackjack:
            {
                var game = room.BlackjackGame;
                if (game.members.FindIndex(x => x.ConnectionId == member.ConnectionId) == -1)
                {
                    game.AddMember(member, _blackjackManager);
                    await Clients.Caller.playblackjack(true);
                    //give time to build component
                    await Task.Delay(500);
                    await _blackjackManager.SendAllUsers(game);
                }

                break;
            }
            case GameMode.NotPlaying:
                break;
        }

        await Clients.Caller.hostupdate(member.ishost);
        var type = await RoomManager.SendPlayerType(room, sendToUsers: false);
        await Clients.Caller.playertype(type);
        if (type != PlayerType.Nothing && !string.IsNullOrWhiteSpace(mainServer.currentVideo?.url) && !mainServer.currentVideo.ended)
            await Clients.Caller.videoupdate(mainServer.currentVideo);
        else if (type != PlayerType.Nothing && mainServer.playlist.Count > 0)
            await Clients.Caller.videoupdate(mainServer.playlist[0]);

        await Clients.Caller.isplayingupdate(mainServer.isplaying);
        await Clients.Caller.timeupdate(mainServer.ToTimeSync());
        await Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
        await Clients.Caller.adduserupdate((int)UserUpdate.Success);
        // The stage, as what it is: a file share has its own player and pause state, which a plain broadcast
        // announcement used to replace with the broadcast viewer.
        if (room.IsFileSharingActive && room.FileShareInitiator != null)
        {
            var sharer = room.server.FindByConnection(room.FileShareInitiator);
            await Clients.Caller.fileShareStarted(room.FileShareInitiator, sharer?.username ?? string.Empty,
                room.uniqueId);
            if (room.IsFileSharePaused) await Clients.Caller.fileSharePlayPause(false);
        }
        else if (room.IsBroadcastOnStage)
        {
            await Clients.Caller.startSfuStream(room.uniqueId, room.CurrentStreamer!);
            if (room.ActiveRecording != null) await Clients.Caller.sfuRecordingStarted(room.ActiveRecording);
        }

        if (room.IsRtmpFileShareActive && !string.IsNullOrWhiteSpace(room.RtmpStreamUrl))
        {
            var rtmpSession = _rtmpFileShareManager.Get(room.uniqueId);
            var rtmpSharer = room.server.members.FirstOrDefault(member => member?.ConnectionId == room.RtmpFileShareInitiator);

            await Clients.Caller.rtmpFileShareStarted(
                room.RtmpFileShareInitiator ?? string.Empty,
                rtmpSharer?.username ?? string.Empty,
                room.uniqueId,
                room.RtmpStreamUrl,
                room.RtmpFileShareDurationSec ?? rtmpSession?.DurationSec);

            await Clients.Caller.rtmpFileShareMetadata(
                room.RtmpFileShareDurationSec ?? rtmpSession?.DurationSec,
                room.RtmpFileShareUsesVodPlayback || string.IsNullOrWhiteSpace(room.RtmpFileShareUploadId));

            if (room.RtmpFileShareUsesVodPlayback)
            {
                await Clients.Caller.rtmpFileShareVodReady(
                    room.RtmpStreamUrl,
                    room.RtmpFileShareDurationSec ?? rtmpSession?.DurationSec,
                    room.RtmpFileSharePlaybackContentType);
            }

            if (rtmpSession != null)
            {
                if (rtmpSession.Paused)
                    await Clients.Caller.rtmpFileSharePlayPause(false, rtmpSession.CurrentPositionSec);
                else
                    await Clients.Caller.rtmpFileSharePosition(rtmpSession.CurrentPositionSec);
            }
        }

        if (mainServer.playlist.Count > 0) await Clients.Caller.playlistupdate(mainServer.playlist);
    }


    [ErrorHandling]
    public async Task ChangeHost(string usernameMember, string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        if (!CheckPrivileges(room))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header = "Error", Question = "You don't have permissions to change the host in this room",
                Answer1 = "Ok"
            });
            return;
        }

        // Prevent host hand-off while a file share is in progress — the initiator
        // needs to stay as host so they can pause, seek, and stop the share.
        if (room.IsFileSharingActive)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Warning)
            {
                Header = "Host Locked",
                Question = "The host cannot be changed while a file share is active. Stop the file share first.",
                Answer1 = "Ok"
            });
            return;
        }

        var mainServer = room.server;
        var successor = mainServer.members.Find(candidate =>
            candidate != null && !candidate.ishost
            && (candidate.username == usernameMember || candidate.ConnectionId == usernameMember));
        if (successor == null) return;

        var game = room.GallowGame;
        if (game is { PlayingGallows: true }) game.UpdateGallowWord(true);

        var previous = mainServer.members.Find(candidate => candidate != null && candidate.ishost);
        if (previous != null)
        {
            previous.ishost = false;
            if (previous.ConnectionId != null)
                await Clients.Client(previous.ConnectionId).hostupdate(false);
        }

        successor.ishost = true;
        if (successor.ConnectionId != null)
            await Clients.Client(successor.ConnectionId).hostupdate(true);

        await Clients.Group(UniqueId).userupdate(mainServer.members.Select(x => x?.ToDTO()).ToList());
    }

    [ErrorHandling]
    public async Task RemoveUser(string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        var mainServer = room.server;
        var member = mainServer.FindByConnection(Context.ConnectionId);
        if (member == null) return;

        // Leaving ends the leaver's broadcast, file share, live stream and seat in the call, exactly as losing
        // the connection does. It used to end none of them.
        await ReleaseMediaAsync(room, Context.ConnectionId);

        var wasHost = member.ishost;
        var promoted = mainServer.Leave(member);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, UniqueId);

        if (mainServer.members.Count > 0)
        {
            var gallowGame = room.GallowGame;
            if (gallowGame is { PlayingGallows: true })
            {
                if (mainServer.members.Count < 2)
                    await PlayGallowsSettings(UniqueId, gallowGame.GameLanguage, gallowGame.GameLength);

                if (wasHost) gallowGame.UpdateGallowWord(true);
            }

            if (promoted?.ConnectionId != null)
                await Clients.Client(promoted.ConnectionId).hostupdate(true);
        }

        if (room.GameMode == GameMode.Chess)
        {
            var game = ChessLogic.GetChessGame(room.uniqueId);
            if (game != null && (game.LightPlayer.ConnectionId == Context.ConnectionId ||
                                 game.DarkPlayer.ConnectionId == Context.ConnectionId))
                await EndChess(room.uniqueId);
        }

        var blackjack = room.BlackjackGame;
        if (blackjack != null)
        {
            var idx = blackjack.members.FindIndex(x => x.ConnectionId == member.ConnectionId);
            if (idx != -1)
            {
                var bjMember = blackjack.members[idx];
                blackjack.members.RemoveAt(idx);
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
                {
                    await _blackjackManager.SendAllUsers(blackjack);
                }
            }

            if (blackjack.members.Count < 1) await _blackjackManager.PlayNewRound(UniqueId);
        }

        await Clients.Group(UniqueId).userupdate(mainServer.members?.Select(x => x?.ToDTO()).ToList());
        await Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
    }

    [ErrorHandling]
    public async Task BanUser(string username, string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        if (!CheckPrivileges(room))
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header = "Error", Question = "You don't have permissions to ban users from this room",
                Answer1 = "Ok"
            });
            return;
        }

        var mainServer = room.server;
        var member = mainServer.members.Find(candidate => candidate?.username == username);
        if (member == null) return;

        // Recorded against the identity, so the ban survives a refresh. Keying it to a connection id made a
        // ban last exactly as long as the connection it was issued against.
        mainServer.BannedIdentities.Add(member.Identity);

        var promoted = mainServer.Leave(member);
        if (promoted?.ConnectionId != null)
            await Clients.Client(promoted.ConnectionId).hostupdate(true);

        // Addressed by connection, not by user identifier: Clients.User expects an IUserIdProvider
        // identifier, so the previous Clients.User(ConnectionId) call never reached anyone.
        if (member.ConnectionId != null)
            await Clients.Client(member.ConnectionId).adduserupdate((int)UserUpdate.Banned);
        member.InvokeKick();
        await Clients.Group(UniqueId).userupdate(mainServer.members.Select(x => x?.ToDTO()).ToList());
        await Clients.All.getrooms(MainManager.GetRooms().ToSummaries());
    }
}