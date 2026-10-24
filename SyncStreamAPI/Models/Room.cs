using System.Collections.Generic;
using System.Threading;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Games.Gallows;
using SyncStreamAPI.Models.GameModels.Blackjack;
using SyncStreamAPI.Models.GameModels.Chess;
using SyncStreamAPI.Models.GameModels.Gallows;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.Models;

public class Room
{
    public Room(string Name, string UnqiueId, bool Deletable, bool Privileged)
    {
        name = Name;
        uniqueId = UnqiueId;
        deletable = Deletable;
        isPrivileged = Privileged;
        _server.RoomId = UnqiueId;
    }

    /// <summary>
    ///     Creation order, used to list rooms the way they were made.
    /// </summary>
    /// <remarks>
    ///     Explicit rather than relying on a collection's incidental ordering. The store used to be a queue,
    ///     which preserved insertion order by accident; keying rooms by id for lookup lost that, because a
    ///     dictionary's values come back in hash order, so the lobby order became arbitrary and new rooms
    ///     turned up wherever they landed instead of at the end.
    /// </remarks>
    [NotMapped] public long Sequence { get; } = Interlocked.Increment(ref _sequenceCounter);

    private static long _sequenceCounter;

    public string uniqueId { get; set; }
    public string name { get; set; }
    public string password { get; set; }
    public bool isPrivileged { get; set; }
    public bool deletable { get; set; } = true;

    /// <summary>
    ///     Held while the room's stage (broadcast or file share) is being claimed or released, so two members
    ///     cannot both take it and a release cannot interleave with a claim.
    /// </summary>
    [NotMapped] public object StageGate { get; } = new();

    /// <summary>The connection whose broadcast or file share holds the room's stage, or null.</summary>
    [NotMapped] public string? CurrentStreamer { get; set; }

    /// <summary>True while the stage is in use: a broadcast or a file share, both of which run through the SFU.</summary>
    [NotMapped] public bool IsStreamingSfu { get; set; }

    /// <summary>True while a room member's screen or camera broadcast is on the stage (as opposed to a file).</summary>
    [NotMapped] public bool IsBroadcastOnStage => CurrentStreamer != null && IsStreamingSfu && !IsFileSharingActive;

    /// <summary>The stage recording in progress, or null when nothing is being recorded.</summary>
    [NotMapped] public SfuRecordingInfo? ActiveRecording { get; set; }

    /// <summary>True while a room member is actively sharing a local file to the room via WebRTC/SFU.</summary>
    [NotMapped] public bool IsFileSharingActive { get; set; }

    /// <summary>Whether the file share is paused, so someone joining mid-share sees the right state.</summary>
    [NotMapped] public bool IsFileSharePaused { get; set; }

    /// <summary>ConnectionId of the member who initiated the current file share (null when none).</summary>
    [NotMapped] public string? FileShareInitiator { get; set; }

    /// <summary>
    /// True when the active file share is driven by the server-side ffmpeg pipeline
    /// (i.e. the file is on the server and mediasoup produces RTP directly).
    /// False means the browser host is doing captureStream() and producing from their end.
    /// </summary>
    [NotMapped] public bool IsServerFileShare { get; set; }

    /// <summary>
    /// The uploadId of the chunked upload that is feeding the active server-side file stream.
    /// Set when the stream is started early (before the full upload completes) so the upload
    /// directory can be cleaned up when the stream stops.
    /// </summary>
    [NotMapped] public string? FileShareUploadId { get; set; }

    /// <summary>True until the upload feeding the server-side file stream has fully arrived.</summary>
    [NotMapped] public bool FileShareUploadInProgress { get; set; }

    // ── RTMP file-share state ──────────────────────────────────────────────────

    /// <summary>
    /// True while an RTMP file-share holds the room's stage. Claimed under <see cref="StageGate" />; the
    /// share itself is RtmpFileShareManager's session for the room.
    /// </summary>
    [NotMapped] public bool IsRtmpFileShareActive { get; set; }
    /// <summary>ConnectionId of the member who initiated the RTMP file-share (null when none).</summary>
    [NotMapped] public string? RtmpFileShareInitiator { get; set; }

    /// <summary>
    /// Connections currently live in the member list (Discord-style opt-in). Several can be live at once;
    /// each viewer chooses whom to watch. Lock the set itself to read or change it.
    /// </summary>
    [NotMapped] public HashSet<string> LiveStreamers { get; } = new HashSet<string>();

    [NotMapped]
    public Server server
    {
        get => _server;
        set => _server = value;
    }

    [NotMapped] private Server _server { get; set; } = new();

    [NotMapped] public GameMode GameMode => GetGameMode();

    [NotMapped]
    public GallowLogic GallowGame
    {
        get
        {
            var gG = GallowGameManager.gallowGames.FirstOrDefault(x => x != null && x.RoomId == uniqueId);
            return gG;
        }
    }

    [NotMapped]
    public BlackjackLogic BlackjackGame
    {
        get
        {
            var bG = BlackjackManager.blackjackGames.FirstOrDefault(x => x != null && x.RoomId == uniqueId);
            return bG;
        }
    }

    private GameMode GetGameMode()
    {
        var gM = GallowGame;
        var bG = BlackjackGame;
        if (gM == null && bG == null && ChessLogic.GetChessGame(uniqueId) != null) return GameMode.Chess;

        return gM == null ? bG == null ? GameMode.NotPlaying : GameMode.Blackjack : GameMode.Gallows;
    }
}