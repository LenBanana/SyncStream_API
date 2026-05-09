using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Games.Gallows;
using SyncStreamAPI.Models.GameModels.Blackjack;
using SyncStreamAPI.Models.GameModels.Chess;
using SyncStreamAPI.Models.GameModels.Gallows;

namespace SyncStreamAPI.Models;

public class Room
{
    public Room(string Name, string UnqiueId, bool Deletable, bool Privileged)
    {
        name = Name;
        uniqueId = UnqiueId;
        deletable = Deletable;
        isPrivileged = Privileged;
    }

    public string uniqueId { get; set; }
    public string name { get; set; }
    public string password { get; set; }
    public bool isPrivileged { get; set; }
    public bool deletable { get; set; } = true;

    [NotMapped] public string? CurrentStreamer { get; set; }

    /// <summary>True when the current stream is routed through the mediasoup SFU (vs P2P).</summary>
    [NotMapped] public bool IsStreamingSfu { get; set; }

    /// <summary>True while a room member is actively sharing a local file to the room via WebRTC/SFU.</summary>
    [NotMapped] public bool IsFileSharingActive { get; set; }

    /// <summary>ConnectionId of the member who initiated the current file share (null when none).</summary>
    [NotMapped] public string? FileShareInitiator { get; set; }

    /// <summary>
    /// True when the active file share is driven by the server-side ffmpeg pipeline
    /// (i.e. the file is on the server and mediasoup produces RTP directly).
    /// False means the browser host is doing captureStream() and producing from their end.
    /// </summary>
    [NotMapped] public bool IsServerFileShare { get; set; }

    /// <summary>
    /// The uploadId of the in-progress chunked upload that is feeding the active server-side
    /// file stream.  Set when the stream is started early (before the full upload completes)
    /// so the upload temp directory can be cleaned up when the stream stops.
    /// </summary>
    [NotMapped] public string? FileShareUploadId { get; set; }

    // ── RTMP file-share state ──────────────────────────────────────────────────

    /// <summary>True while an RTMP file-share is active in this room.</summary>
    [NotMapped] public bool IsRtmpFileShareActive { get; set; }
    /// <summary>ConnectionId of the member who initiated the RTMP file-share (null when none).</summary>
    [NotMapped] public string? RtmpFileShareInitiator { get; set; }
    /// <summary>uploadId that is still being chunked; null once the upload is finalised.</summary>
    [NotMapped] public string? RtmpFileShareUploadId { get; set; }
    /// <summary>Stable asset key for the active RTMP share, used for HLS handoff and cleanup.</summary>
    [NotMapped] public string? RtmpFileShareAssetKey { get; set; }
    /// <summary>HLS/FLV stream URL broadcast to late-joining viewers.</summary>
    [NotMapped] public string? RtmpStreamUrl { get; set; }
    /// <summary>True once the room has switched from RTMP transport to VOD playback.</summary>
    [NotMapped] public bool RtmpFileShareUsesVodPlayback { get; set; }
    /// <summary>Best-known duration for the active RTMP/VOD share.</summary>
    [NotMapped] public double? RtmpFileShareDurationSec { get; set; }
    /// <summary>MIME type for the current RTMP/VOD playback URL when known.</summary>
    [NotMapped] public string? RtmpFileSharePlaybackContentType { get; set; }

    /// <summary>
    /// Set of connectionIds that are currently streaming P2P (Discord-style opt-in).
    /// Multiple concurrent streamers are supported; each viewer connects on-demand.
    /// </summary>
    [NotMapped] public HashSet<string> ActiveStreamers { get; } = new HashSet<string>();

    /// <summary>
    /// Subset of <see cref="ActiveStreamers"/> that are routed through the SFU
    /// rather than the legacy P2P watcher path.
    /// </summary>
    [NotMapped] public HashSet<string> ActiveSfuStreamers { get; } = new HashSet<string>();

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