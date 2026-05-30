using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Helper.Streaming;

/// <summary>
/// Manages the lifecycle of RTMP file-share sessions: spawning/killing ffmpeg processes,
/// tracking playback position, and broadcasting position ticks to rooms via SignalR.
/// Registered as a singleton in DI.
/// </summary>
public class RtmpFileShareManager : IDisposable
{
    private const string SubtitleBurnMethodText = "text";
    private const string SubtitleBurnMethodBitmap = "bitmap";

    private static readonly string[] DefaultAudioLanguagePriority =
    {
        RtmpPlaybackPreferences.JapaneseLanguage,
        RtmpPlaybackPreferences.EnglishLanguage,
        RtmpPlaybackPreferences.GermanLanguage,
        RtmpPlaybackPreferences.OtherLanguage,
    };
    private static readonly HashSet<string> JapaneseAudioLanguages = new(StringComparer.OrdinalIgnoreCase)
        { "jpn", "ja", "jp" };
    private static readonly HashSet<string> EnglishLanguages = new(StringComparer.OrdinalIgnoreCase)
        { "eng", "en" };
    private static readonly HashSet<string> GermanLanguages = new(StringComparer.OrdinalIgnoreCase)
        { "deu", "ger", "de" };
    private static readonly HashSet<string> TextSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
        { "subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text" };
    private static readonly HashSet<string> BitmapSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
        { "hdmv_pgs_subtitle", "pgs", "dvd_subtitle", "dvdsub", "vobsub", "xsub", "dvb_subtitle" };
    private static readonly string[] PreferredSubtitleTitleKeywords =
    {
        "full subtitles",
        "full subtitle",
        "dialogue",
        "dialog",
        "english subs",
        "english subtitles",
        "subtitles",
    };
    private static readonly string[] DeprioritizedSubtitleTitleKeywords =
    {
        "episode name",
        "signs",
        "songs",
        "sign/song",
        "title",
        "karaoke",
        "forced",
    };

    private readonly string _rtmpBaseUrl;
    private readonly IHubContext<ServerHub, IServerHub> _hub;
    private readonly ConcurrentDictionary<string, RtmpFileShareSession> _sessions = new();
    private readonly Timer _positionTimer;
    private readonly int _maxVideoWidth;
    private readonly int _videoBitrateKbps;
    private readonly int _maxVideoBitrateKbps;
    private readonly int _videoBufferSizeKbps;
    private readonly int _audioBitrateKbps;
    private readonly string _videoPreset;

    public RtmpFileShareManager(IConfiguration configuration, IHubContext<ServerHub, IServerHub> hub)
    {
        _hub = hub;
        // e.g. "rtmp://rtmp-server:1935/live"
        _rtmpBaseUrl = configuration["RtmpUrl"]?.TrimEnd('/') ?? "rtmp://rtmp-server:1935/live";
        _maxVideoWidth = ParsePositiveInt(configuration["RtmpFileShare:MaxWidth"], 1280);
        _videoBitrateKbps = ParsePositiveInt(configuration["RtmpFileShare:VideoBitrateKbps"], 3500);
        _maxVideoBitrateKbps = ParsePositiveInt(configuration["RtmpFileShare:MaxVideoBitrateKbps"], 4200);
        _videoBufferSizeKbps = ParsePositiveInt(configuration["RtmpFileShare:VideoBufferSizeKbps"], _maxVideoBitrateKbps * 2);
        _audioBitrateKbps = ParsePositiveInt(configuration["RtmpFileShare:AudioBitrateKbps"], 128);
        _videoPreset = string.IsNullOrWhiteSpace(configuration["RtmpFileShare:VideoPreset"])
            ? "superfast"
            : configuration["RtmpFileShare:VideoPreset"]!.Trim();
        // Broadcast position to all rooms every 5 seconds.
        _positionTimer = new Timer(OnPositionTick, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public RtmpFileShareSession? Get(string roomId)
        => _sessions.TryGetValue(roomId, out var s) ? s : null;

    public bool TryConsumeExpectedPublishDone(string streamToken, string streamName)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.StreamToken != streamToken ||
                !string.Equals(session.OwnerUsername, streamName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lock (session)
            {
                if (session.PendingPublisherDisconnects < 1)
                    return false;

                session.PendingPublisherDisconnects--;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates a session, probes file duration, and spawns ffmpeg.
    /// </summary>
    public async Task<RtmpFileShareSession> StartAsync(
        string roomId, string ownerConnectionId, int userId, string username,
        string streamToken, string filePath, string? uploadId, RtmpPlaybackPreferences? playbackPreferences)
    {
        var normalizedPreferences = NormalizePlaybackPreferences(playbackPreferences);
        var fileInfo = new FileInfo(filePath);
        Console.WriteLine(
            $"{GetLogPrefix(roomId)} start requested file=\"{fileInfo.Name}\" sizeBytes={fileInfo.Length} " +
            $"subtitleMode={normalizedPreferences.SubtitleMode} " +
            $"audioPriority=[{string.Join(",", normalizedPreferences.AudioLanguagePriority)}]");
        var playbackSelection = await ProbePlaybackSelectionAsync(filePath, normalizedPreferences);

        var session = new RtmpFileShareSession
        {
            RoomId            = roomId,
            OwnerConnectionId = ownerConnectionId,
            OwnerUserId       = userId,
            OwnerUsername     = username.ToLower(),
            StreamToken       = streamToken,
            FilePath          = filePath,
            UploadId          = uploadId,
            PositionSec       = 0,
            Paused            = false,
            PlaybackPreferences = normalizedPreferences,
        };

        ApplyPlaybackSelection(session, playbackSelection);

        _sessions[roomId] = session;

        // Probe duration in the background — does not block stream start.
        _ = Task.Run(async () =>
        {
            try
            {
                session.DurationSec = await ProbeFileDurationAsync(filePath);
                var room = MainManager.GetRoom(roomId);
                if (room != null)
                    room.RtmpFileShareDurationSec = session.DurationSec;
                Console.WriteLine($"{GetLogPrefix(roomId)} durationProbe durationSec={(session.DurationSec?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")}");
                await _hub.Clients.Group(roomId).rtmpFileShareMetadata(session.DurationSec, string.IsNullOrWhiteSpace(session.UploadId));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{GetLogPrefix(roomId)} durationProbe failed: {ex.Message}");
            }
        });

        SpawnFfmpeg(session, 0);
        return session;
    }

    /// <summary>Toggles play/pause for the session.</summary>
    public Task PausePlayAsync(string roomId, bool isPlaying)
    {
        if (!_sessions.TryGetValue(roomId, out var session)) return Task.CompletedTask;

        Console.WriteLine($"{GetLogPrefix(roomId)} {(isPlaying ? "resume" : "pause")} requested currentPositionSec={session.CurrentPositionSec:F3}");

        if (isPlaying && session.Paused)
        {
            session.Paused = false;
            session.RetryCount = 0; // explicit resume = fresh attempt
            SpawnFfmpeg(session, session.PositionSec);
        }
        else if (!isPlaying && !session.Paused)
        {
            RegisterExpectedPublisherDisconnect(session);
            KillFfmpeg(session);
            session.Paused = true;
        }
        return Task.CompletedTask;
    }

    /// <summary>Kills the current ffmpeg process and restarts from the new position.</summary>
    public Task SeekAsync(string roomId, double positionSec)
    {
        if (!_sessions.TryGetValue(roomId, out var session)) return Task.CompletedTask;
        Console.WriteLine($"{GetLogPrefix(roomId)} seek requested targetPositionSec={positionSec:F3} currentPositionSec={session.CurrentPositionSec:F3}");
        RegisterExpectedPublisherDisconnect(session);
        KillFfmpeg(session);
        session.PositionSec = positionSec;
        session.Paused = false;
        session.RetryCount = 0; // new position = fresh attempt
        SpawnFfmpeg(session, positionSec);
        return Task.CompletedTask;
    }

    /// <summary>Stops the session, kills ffmpeg, cleans up the upload directory.</summary>
    /// <returns>True if a session was actually stopped.</returns>
    public bool Stop(string roomId)
    {
        if (!_sessions.TryRemove(roomId, out var session)) return false;
        Console.WriteLine($"{GetLogPrefix(roomId)} stop requested finalPositionSec={session.CurrentPositionSec:F3} retries={session.RetryCount}");
        KillFfmpeg(session);
        if (!session.PreserveUploadArtifacts)
            CleanUpUploadDir(session);
        return true;
    }

    /// <summary>Clears the upload-guard so seeking becomes available.</summary>
    public void MarkUploadComplete(string roomId, string uploadId)
    {
        if (_sessions.TryGetValue(roomId, out var session) && session.UploadId == uploadId)
        {
            session.UploadId = null;
            var room = MainManager.GetRoom(roomId);
            if (room != null)
                room.RtmpFileShareDurationSec = session.DurationSec;
            Console.WriteLine($"{GetLogPrefix(roomId)} upload completed uploadId={uploadId}");
            _ = _hub.Clients.Group(roomId).rtmpFileShareMetadata(session.DurationSec, canSeek: true);
        }
    }

    public bool TransitionToVodPlayback(string roomId)
    {
        if (!_sessions.TryGetValue(roomId, out var session))
            return false;

        session.PreserveUploadArtifacts = true;
        RegisterExpectedPublisherDisconnect(session);
        KillFfmpeg(session);
        session.Paused = true;
        return true;
    }

    public bool TryMarkPublisherLive(string streamToken, string streamName, out string roomId, out double positionSec)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.StreamToken != streamToken ||
                !string.Equals(session.OwnerUsername, streamName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            session.IsPublisherLive = true;
            roomId = session.RoomId;
            positionSec = session.CurrentPositionSec;
            return true;
        }

        roomId = string.Empty;
        positionSec = 0;
        return false;
    }

    // ── ffmpeg process management ─────────────────────────────────────────────

    private void SpawnFfmpeg(RtmpFileShareSession session, double fromPositionSec)
    {
        RefreshPlaybackSelectionIfNeeded(session);

        var seekArg = fromPositionSec > 0.5
            ? $"-ss {fromPositionSec.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} "
            : string.Empty;
        var videoMapAndFilterArg = BuildVideoMapAndFilterArg(session);

        var rtmpUrl = $"{_rtmpBaseUrl}/{session.OwnerUsername}?token={Uri.EscapeDataString(session.StreamToken)}";

        var args = string.Concat(
            "-nostats -progress pipe:1 -stats_period 1 -threads 0 ",
            $"-re {seekArg}",
            $"-i \"{session.FilePath}\" ",
            videoMapAndFilterArg,
            $"-map {session.AudioMapSpecifier} ",
            $"-c:v libx264 -preset {_videoPreset} -tune zerolatency ",
            "-profile:v main -level:v 4.1 -pix_fmt yuv420p ",
            $"-b:v {_videoBitrateKbps}k -maxrate {_maxVideoBitrateKbps}k -bufsize {_videoBufferSizeKbps}k ",
            "-g 48 -keyint_min 48 ",
            "-x264-params scenecut=0:repeat-headers=1 ",
            $"-c:a aac -b:a {_audioBitrateKbps}k -ar 48000 -ac 2 ",
            $"-f flv \"{rtmpUrl}\""
        );

        Console.WriteLine(
            $"{GetLogPrefix(session.RoomId)} spawn ffmpeg seek={fromPositionSec:F3}s widthCap={_maxVideoWidth} preset={_videoPreset} " +
            $"video={_videoBitrateKbps}/{_maxVideoBitrateKbps}k audio={_audioBitrateKbps}k " +
            $"audioTrack=\"{session.AudioSelectionLabel}\" subtitleTrack=\"{session.SubtitleSelectionLabel ?? "none"}\" " +
            $"subtitleBurn={session.SubtitleBurnMethod ?? "none"}");

        session.IsPublisherLive = false;
        session.LastEncoderOutTimeSec = 0;
        session.LastEncoderProgressAtUtc = null;

        var progressFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = General.GetFFmpegPath(),
                Arguments              = args,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            var separatorIndex = e.Data.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == e.Data.Length - 1)
            {
                Console.WriteLine($"{GetLogPrefix(session.RoomId)} ffmpeg progress(raw) {e.Data}");
                return;
            }

            var key = e.Data[..separatorIndex];
            var value = e.Data[(separatorIndex + 1)..];
            progressFields[key] = value;

            if (string.Equals(key, "progress", StringComparison.OrdinalIgnoreCase))
            {
                LogFfmpegProgress(session, progressFields);
                progressFields.Clear();
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"{GetLogPrefix(session.RoomId)} ffmpeg stderr {e.Data}");
        };

        process.Exited += (_, _) =>
        {
            // KillFfmpeg sets session.Process = null before killing.
            // If this process is no longer session.Process, it was an intentional kill — ignore.
            if (!ReferenceEquals(session.Process, process)) return;

            var elapsed = (DateTime.UtcNow - session.StartedAtUtc).TotalSeconds;
            session.Process = null;

            if (!_sessions.ContainsKey(session.RoomId) || session.Paused) return;

            const int maxRetries = 8;
            if (session.RetryCount < maxRetries)
            {
                session.RetryCount++;
                // Back-off: 3s, 6s, 9s, 12s, … so later retries give the background
                // upload more time to flush the moov atom / file tail to disk.
                var delaySec = session.RetryCount * 3;
                Console.WriteLine($"{GetLogPrefix(session.RoomId)} ffmpeg exited exitCode={process.ExitCode} elapsedSec={elapsed:F1} " +
                                  $"(retry {session.RetryCount}/{maxRetries}, next in {delaySec}s)");
                Task.Run(async () =>
                {
                    await Task.Delay(delaySec * 1000);
                    if (_sessions.ContainsKey(session.RoomId) && !session.Paused && session.Process == null)
                        SpawnFfmpeg(session, session.PositionSec);
                });
            }
            else
            {
                Console.WriteLine($"{GetLogPrefix(session.RoomId)} ffmpeg exited exitCode={process.ExitCode} elapsedSec={elapsed:F1} " +
                                  $"and exhausted {maxRetries} retries; giving up");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        session.Process      = process;
        session.StartedAtUtc = DateTime.UtcNow;
    }

    private static void RegisterExpectedPublisherDisconnect(RtmpFileShareSession session)
    {
        lock (session)
        {
            session.PendingPublisherDisconnects++;
            Console.WriteLine($"{GetLogPrefix(session.RoomId)} expected publisher disconnect count={session.PendingPublisherDisconnects}");
        }
    }

    private string BuildVideoMapAndFilterArg(RtmpFileShareSession session)
    {
        var scaleFilter = _maxVideoWidth > 0
            ? $"scale=w='min(iw,{_maxVideoWidth})':h=-2"
            : null;

        if (session.SubtitleOrdinal.HasValue &&
            string.Equals(session.SubtitleBurnMethod, SubtitleBurnMethodBitmap, StringComparison.OrdinalIgnoreCase))
        {
            var postOverlayScale = string.IsNullOrWhiteSpace(scaleFilter)
                ? string.Empty
                : $",{scaleFilter}";
            return $"-filter_complex \"[0:v:0]split[base][ref];[0:s:{session.SubtitleOrdinal.Value}][ref]scale2ref=w=iw:h=ih[sub][unused];[unused]nullsink;[base][sub]overlay=eof_action=pass{postOverlayScale}[v]\" -map \"[v]\" ";
        }

        var filters = new List<string>();

        if (session.SubtitleOrdinal.HasValue &&
            string.Equals(session.SubtitleBurnMethod, SubtitleBurnMethodText, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(session.SubtitleFilter))
        {
            filters.Add(session.SubtitleFilter);
        }

        if (!string.IsNullOrWhiteSpace(scaleFilter))
            filters.Add(scaleFilter);

        return filters.Count == 0
            ? "-map 0:v:0 "
            : $"-map 0:v:0 -vf \"{string.Join(",", filters)}\" ";
    }

    private static int ParsePositiveInt(string? rawValue, int fallback)
        => int.TryParse(rawValue, out var value) && value > 0 ? value : fallback;

    private static void KillFfmpeg(RtmpFileShareSession session)
    {
        var p = session.Process;
        if (p == null) return;
        // Save position before killing so seek/pause resume from the right offset.
        session.PositionSec = session.CurrentPositionSec;
        session.Process = null;
        session.IsPublisherLive = false;
        session.LastEncoderOutTimeSec = 0;
        session.LastEncoderProgressAtUtc = null;
        Console.WriteLine($"{GetLogPrefix(session.RoomId)} kill ffmpeg savedPositionSec={session.PositionSec:F3}");
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(2000);
            }
        }
        catch { /* best-effort */ }
        finally { p.Dispose(); }
    }

    private void RefreshPlaybackSelectionIfNeeded(RtmpFileShareSession session)
    {
        if (session.PlaybackSelectionResolved)
            return;

        try
        {
            var refreshedSelection = ProbePlaybackSelectionAsync(session.FilePath, session.PlaybackPreferences)
                .GetAwaiter()
                .GetResult();

            if (!refreshedSelection.ProbeSucceeded)
                return;

            ApplyPlaybackSelection(session, refreshedSelection);
            Console.WriteLine(
                $"{GetLogPrefix(session.RoomId)} playback selection refreshed " +
                $"audioTrack=\"{session.AudioSelectionLabel}\" subtitleTrack=\"{session.SubtitleSelectionLabel ?? "none"}\"");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{GetLogPrefix(session.RoomId)} playback selection refresh failed: {ex.Message}");
        }
    }

    private static void ApplyPlaybackSelection(RtmpFileShareSession session, RtmpPlaybackSelection playbackSelection)
    {
        session.AudioMapSpecifier = playbackSelection.AudioMapSpecifier;
        session.AudioSelectionLabel = playbackSelection.AudioSelectionLabel;
        session.SubtitleFilter = playbackSelection.SubtitleFilter;
        session.SubtitleOrdinal = playbackSelection.SubtitleOrdinal;
        session.SubtitleBurnMethod = playbackSelection.SubtitleBurnMethod;
        session.SubtitleSelectionLabel = playbackSelection.SubtitleSelectionLabel;
        session.PlaybackSelectionResolved = playbackSelection.ProbeSucceeded;
    }

    private static void CleanUpUploadDir(RtmpFileShareSession session)
    {
        var dir = Path.GetDirectoryName(session.FilePath);
        if (string.IsNullOrWhiteSpace(dir)) return;
        // Retry a few times — the background upload may still hold the file open
        // for a brief moment after Stop is called.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                return;
            }
            catch (IOException)
            {
                if (attempt < 3) Thread.Sleep(500 * (attempt + 1));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RtmpShare] Failed to delete upload dir: {ex.Message}");
                return;
            }
        }
        Console.WriteLine($"[RtmpShare] Upload dir still locked after retries, skipping cleanup: {dir}");
    }

    // ── ffprobe duration probe ────────────────────────────────────────────────

    private async Task<RtmpPlaybackSelection> ProbePlaybackSelectionAsync(
        string filePath,
        RtmpPlaybackPreferences playbackPreferences)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = General.GetFFprobePath(),
                    Arguments = $"-v error -print_format json -show_entries stream=index,codec_type,codec_name:stream_tags=language,title:stream_disposition=default,forced \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            p.Start();
            var stdout = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var stderr = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await p.WaitForExitAsync().ConfigureAwait(false);

            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                Console.WriteLine(
                    $"[RtmpShare/Probe] ffprobe failed exitCode={p.ExitCode} stdoutEmpty={string.IsNullOrWhiteSpace(stdout)} " +
                    $"stderr=\"{TrimForLog(stderr, 400)}\"");
                return BuildExplicitFallbackSelection(filePath, playbackPreferences, "ffprobe failed");
            }

            var streams = JsonNode.Parse(stdout)?["streams"]?.AsArray();
            if (streams == null || streams.Count == 0)
            {
                Console.WriteLine("[RtmpShare/Probe] ffprobe returned no streams");
                return BuildExplicitFallbackSelection(filePath, playbackPreferences, "ffprobe returned no streams");
            }

            var parsedStreams = new List<RtmpProbeStream>();
            var audioOrdinal = 0;
            var subtitleOrdinal = 0;

            foreach (var stream in streams)
            {
                if (stream == null) continue;

                var codecType = stream["codec_type"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(codecType)) continue;

                var parsed = new RtmpProbeStream
                {
                    StreamIndex = ReadInt(stream, "index") ?? -1,
                    CodecType = codecType,
                    CodecName = ReadString(stream, "codec_name") ?? string.Empty,
                    Language = NormalizeLanguage(ReadNestedString(stream, "tags", "language")),
                    Title = ReadNestedString(stream, "tags", "title") ?? string.Empty,
                    IsDefault = (ReadNestedInt(stream, "disposition", "default") ?? 0) == 1,
                    IsForced = (ReadNestedInt(stream, "disposition", "forced") ?? 0) == 1,
                };

                if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
                    parsed.AudioOrdinal = audioOrdinal++;

                if (string.Equals(codecType, "subtitle", StringComparison.OrdinalIgnoreCase))
                    parsed.SubtitleOrdinal = subtitleOrdinal++;

                parsedStreams.Add(parsed);
            }

            var preferredSubtitle = SelectSubtitleTrack(parsedStreams, playbackPreferences);
            var audio = SelectAudioTrack(parsedStreams, playbackPreferences, preferredSubtitle != null);
            var subtitle = ResolveSubtitleTrack(preferredSubtitle, audio, playbackPreferences);
            var subtitleBurnMethod = subtitle != null ? GetSubtitleBurnMethod(subtitle.CodecName) : null;

            Console.WriteLine(
                $"[RtmpShare/Probe] subtitleMode={playbackPreferences.SubtitleMode} " +
                $"selectedAudioOrdinal={(playbackPreferences.SelectedAudioOrdinal?.ToString() ?? "none")} " +
                $"selectedSubtitleOrdinal={(playbackPreferences.SelectedSubtitleOrdinal?.ToString() ?? "none")} " +
                $"audioPriority=[{string.Join(",", playbackPreferences.AudioLanguagePriority)}] " +
                $"audioCandidates=[{string.Join(" | ", parsedStreams.Where(s => string.Equals(s.CodecType, "audio", StringComparison.OrdinalIgnoreCase)).Select(DescribeStream))}] " +
                $"subtitleCandidates=[{string.Join(" | ", parsedStreams.Where(s => string.Equals(s.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase)).Select(DescribeStream))}] " +
                $"selectedAudio=\"{(audio != null ? DescribeStream(audio) : "first audio fallback")}\" " +
                $"selectedSubtitle=\"{(subtitle != null ? DescribeStream(subtitle) : "none")}\"");

            return new RtmpPlaybackSelection
            {
                ProbeSucceeded = true,
                AudioMapSpecifier = BuildAudioMapSpecifier(audio),
                AudioSelectionLabel = audio != null ? DescribeStream(audio) : "first audio fallback",
                SubtitleFilter = string.Equals(subtitleBurnMethod, SubtitleBurnMethodText, StringComparison.OrdinalIgnoreCase)
                    ? BuildSubtitleFilter(filePath, subtitle!.SubtitleOrdinal)
                    : null,
                SubtitleOrdinal = subtitle?.SubtitleOrdinal,
                SubtitleBurnMethod = subtitleBurnMethod,
                SubtitleSelectionLabel = subtitle != null ? DescribeStream(subtitle) : null,
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RtmpShare] ffprobe track selection failed: {ex.Message}");
            return BuildExplicitFallbackSelection(filePath, playbackPreferences, "ffprobe exception");
        }
    }

    private static RtmpPlaybackSelection BuildExplicitFallbackSelection(
        string filePath,
        RtmpPlaybackPreferences playbackPreferences,
        string reason)
    {
        var audioMapSpecifier = playbackPreferences.SelectedAudioOrdinal.HasValue
            ? $"0:a:{playbackPreferences.SelectedAudioOrdinal.Value}?"
            : "0:a:0?";
        var audioSelectionLabel = playbackPreferences.SelectedAudioOrdinal.HasValue
            ? $"client-selected audio ordinal={playbackPreferences.SelectedAudioOrdinal.Value} ({reason})"
            : $"first audio fallback ({reason})";

        if (string.Equals(playbackPreferences.SubtitleMode, RtmpPlaybackPreferences.SubtitleModeNever, StringComparison.OrdinalIgnoreCase) ||
            !playbackPreferences.SelectedSubtitleOrdinal.HasValue)
        {
            return new RtmpPlaybackSelection
            {
                AudioMapSpecifier = audioMapSpecifier,
                AudioSelectionLabel = audioSelectionLabel,
            };
        }

        var burnMethod = GetSubtitleBurnMethod(playbackPreferences.SelectedSubtitleCodecName);
        if (burnMethod == null)
        {
            return new RtmpPlaybackSelection
            {
                AudioMapSpecifier = audioMapSpecifier,
                AudioSelectionLabel = audioSelectionLabel,
            };
        }

        var subtitleOrdinal = playbackPreferences.SelectedSubtitleOrdinal.Value;
        return new RtmpPlaybackSelection
        {
            AudioMapSpecifier = audioMapSpecifier,
            AudioSelectionLabel = audioSelectionLabel,
            SubtitleFilter = string.Equals(burnMethod, SubtitleBurnMethodText, StringComparison.OrdinalIgnoreCase)
                ? BuildSubtitleFilter(filePath, subtitleOrdinal)
                : null,
            SubtitleOrdinal = subtitleOrdinal,
            SubtitleBurnMethod = burnMethod,
            SubtitleSelectionLabel = $"client-selected subtitle ordinal={subtitleOrdinal} codec={playbackPreferences.SelectedSubtitleCodecName ?? "unknown"} ({reason})",
        };
    }

    private static async Task<double?> ProbeFileDurationAsync(string filePath)
    {
        var tcs = new TaskCompletionSource<double?>();
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = General.GetFFprobePath(),
                Arguments              = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            }
        };

        p.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data) &&
                double.TryParse(e.Data.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var d))
            {
                tcs.TrySetResult(d);
            }
        };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        cts.Token.Register(() => tcs.TrySetResult(null));

        await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        tcs.TrySetResult(null);
        return await tcs.Task;
    }

    private static RtmpProbeStream? SelectAudioTrack(
        IEnumerable<RtmpProbeStream> streams,
        RtmpPlaybackPreferences playbackPreferences,
        bool subtitleAvailable)
    {
        var audioStreams = streams
            .Where(stream => string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase) && stream.StreamIndex >= 0)
            .ToList();

        if (audioStreams.Count == 0)
        {
            return playbackPreferences.SelectedAudioOrdinal.HasValue
                ? new RtmpProbeStream
                {
                    StreamIndex = -1,
                    AudioOrdinal = playbackPreferences.SelectedAudioOrdinal.Value,
                    CodecType = "audio",
                    CodecName = "unknown",
                    Language = "und",
                    Title = "client-selected audio"
                }
                : null;
        }

        if (playbackPreferences.SelectedAudioOrdinal.HasValue)
        {
            var selected = audioStreams.FirstOrDefault(stream => stream.AudioOrdinal == playbackPreferences.SelectedAudioOrdinal.Value);
            if (selected != null)
                return selected;
        }

        return audioStreams
            .OrderBy(stream => GetAudioLanguagePreferenceRank(stream.Language, playbackPreferences, subtitleAvailable))
            .ThenBy(stream => stream.IsDefault ? 0 : 1)
            .ThenBy(stream => stream.StreamIndex)
            .FirstOrDefault();
    }

    private static RtmpProbeStream? ResolveSubtitleTrack(
        RtmpProbeStream? preferredSubtitle,
        RtmpProbeStream? selectedAudio,
        RtmpPlaybackPreferences playbackPreferences)
    {
        if (preferredSubtitle == null)
            return null;

        if (string.Equals(playbackPreferences.SubtitleMode, RtmpPlaybackPreferences.SubtitleModeNever, StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.Equals(playbackPreferences.SubtitleMode, RtmpPlaybackPreferences.SubtitleModeAlways, StringComparison.OrdinalIgnoreCase))
            return preferredSubtitle;

        return selectedAudio != null &&
               string.Equals(GetPreferenceLanguageKey(selectedAudio.Language), RtmpPlaybackPreferences.JapaneseLanguage, StringComparison.OrdinalIgnoreCase)
            ? preferredSubtitle
            : null;
    }

    private static RtmpProbeStream? SelectSubtitleTrack(IEnumerable<RtmpProbeStream> streams, RtmpPlaybackPreferences playbackPreferences)
    {
        var subtitleStreams = streams
            .Where(stream =>
                string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase) &&
                stream.StreamIndex >= 0 &&
                GetSubtitleBurnMethod(stream.CodecName) != null)
            .ToList();

        if (playbackPreferences.SelectedSubtitleOrdinal.HasValue &&
            !string.Equals(playbackPreferences.SubtitleMode, RtmpPlaybackPreferences.SubtitleModeNever, StringComparison.OrdinalIgnoreCase))
        {
            var selected = subtitleStreams.FirstOrDefault(stream => stream.SubtitleOrdinal == playbackPreferences.SelectedSubtitleOrdinal.Value);
            if (selected != null)
                return selected;

            var burnMethod = GetSubtitleBurnMethod(playbackPreferences.SelectedSubtitleCodecName);
            if (burnMethod != null)
            {
                return new RtmpProbeStream
                {
                    StreamIndex = -1,
                    SubtitleOrdinal = playbackPreferences.SelectedSubtitleOrdinal.Value,
                    CodecType = "subtitle",
                    CodecName = playbackPreferences.SelectedSubtitleCodecName ?? "unknown",
                    Language = "und",
                    Title = "client-selected subtitle"
                };
            }
        }

        if (subtitleStreams.Count == 0)
            return null;

        return subtitleStreams
            .OrderBy(stream => GetSubtitleLanguagePreferenceRank(stream.Language))
            .ThenBy(stream => GetSubtitleTitlePreferenceRank(stream.Title))
            .ThenBy(stream => stream.IsDefault ? 0 : 1)
            .ThenBy(stream => stream.IsForced ? 1 : 0)
            .ThenBy(stream => stream.StreamIndex)
            .FirstOrDefault();
    }

    private static int GetAudioLanguagePreferenceRank(
        string language,
        RtmpPlaybackPreferences playbackPreferences,
        bool subtitleAvailable)
    {
        var languageKey = GetPreferenceLanguageKey(language);
        var index = playbackPreferences.AudioLanguagePriority
            .FindIndex(item => string.Equals(item, languageKey, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
            index = playbackPreferences.AudioLanguagePriority.Count;

        var japaneseNeedsSubtitle =
            string.Equals(playbackPreferences.SubtitleMode, RtmpPlaybackPreferences.SubtitleModeAuto, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(languageKey, RtmpPlaybackPreferences.JapaneseLanguage, StringComparison.OrdinalIgnoreCase) &&
            !subtitleAvailable;

        return japaneseNeedsSubtitle ? index + playbackPreferences.AudioLanguagePriority.Count : index;
    }

    private static int GetSubtitleLanguagePreferenceRank(string language)
    {
        if (EnglishLanguages.Contains(language)) return 0;
        if (GermanLanguages.Contains(language)) return 1;
        return 2;
    }

    private static int GetSubtitleTitlePreferenceRank(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return 1;

        var normalizedTitle = title.Trim().ToLowerInvariant();

        if (PreferredSubtitleTitleKeywords.Any(keyword => normalizedTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            return 0;

        if (DeprioritizedSubtitleTitleKeywords.Any(keyword => normalizedTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            return 2;

        return 1;
    }

    private static string GetPreferenceLanguageKey(string language)
    {
        if (JapaneseAudioLanguages.Contains(language)) return RtmpPlaybackPreferences.JapaneseLanguage;
        if (EnglishLanguages.Contains(language)) return RtmpPlaybackPreferences.EnglishLanguage;
        if (GermanLanguages.Contains(language)) return RtmpPlaybackPreferences.GermanLanguage;
        return RtmpPlaybackPreferences.OtherLanguage;
    }

    private static RtmpPlaybackPreferences NormalizePlaybackPreferences(RtmpPlaybackPreferences? playbackPreferences)
    {
        var normalized = new List<string>();
        foreach (var item in playbackPreferences?.AudioLanguagePriority ?? Enumerable.Empty<string>())
        {
            var key = NormalizePreferenceLanguage(item);
            if (key != null && !normalized.Contains(key, StringComparer.OrdinalIgnoreCase))
                normalized.Add(key);
        }

        foreach (var fallback in DefaultAudioLanguagePriority)
        {
            if (!normalized.Contains(fallback, StringComparer.OrdinalIgnoreCase))
                normalized.Add(fallback);
        }

        return new RtmpPlaybackPreferences
        {
            SubtitleMode = NormalizeSubtitleMode(playbackPreferences?.SubtitleMode),
            AudioLanguagePriority = normalized,
            SelectedAudioOrdinal = NormalizeTrackOrdinal(playbackPreferences?.SelectedAudioOrdinal),
            SelectedSubtitleOrdinal = NormalizeTrackOrdinal(playbackPreferences?.SelectedSubtitleOrdinal),
            SelectedSubtitleCodecName = NormalizeSubtitleCodecName(playbackPreferences?.SelectedSubtitleCodecName),
        };
    }

    private static int? NormalizeTrackOrdinal(int? ordinal)
        => ordinal.HasValue && ordinal.Value >= 0 && ordinal.Value < 256 ? ordinal.Value : null;

    private static string NormalizeSubtitleMode(string? subtitleMode)
    {
        return subtitleMode?.Trim().ToLowerInvariant() switch
        {
            RtmpPlaybackPreferences.SubtitleModeAlways => RtmpPlaybackPreferences.SubtitleModeAlways,
            RtmpPlaybackPreferences.SubtitleModeNever => RtmpPlaybackPreferences.SubtitleModeNever,
            _ => RtmpPlaybackPreferences.SubtitleModeAuto,
        };
    }

    private static string? NormalizePreferenceLanguage(string? language)
    {
        return language?.Trim().ToLowerInvariant() switch
        {
            RtmpPlaybackPreferences.JapaneseLanguage => RtmpPlaybackPreferences.JapaneseLanguage,
            RtmpPlaybackPreferences.EnglishLanguage => RtmpPlaybackPreferences.EnglishLanguage,
            RtmpPlaybackPreferences.GermanLanguage => RtmpPlaybackPreferences.GermanLanguage,
            RtmpPlaybackPreferences.OtherLanguage => RtmpPlaybackPreferences.OtherLanguage,
            _ => null,
        };
    }

    private static string NormalizeLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) ? "und" : language.Trim().ToLowerInvariant();

    private static string? NormalizeSubtitleCodecName(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName))
            return null;

        var normalized = codecName.Trim().ToLowerInvariant();
        var compact = normalized.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).Replace("/", string.Empty);

        if (compact.Contains("pgs") || compact.Contains("hdmv")) return "hdmv_pgs_subtitle";
        if (compact.Contains("vobsub") || compact.Contains("dvdsubtitle") || compact.Contains("dvdsub")) return "dvd_subtitle";
        if (compact.Contains("dvbsubtitle") || compact.Contains("dvbsub")) return "dvb_subtitle";
        if (compact.Contains("xsub")) return "xsub";
        if (compact.Contains("ass")) return "ass";
        if (compact.Contains("ssa")) return "ssa";
        if (compact.Contains("webvtt") || compact.Contains("vtt")) return "webvtt";
        if (compact.Contains("movtext") || compact.Contains("tx3g")) return "mov_text";
        if (compact.Contains("subrip") || compact.Contains("srt") || compact.Contains("utf8")) return "subrip";
        if (compact.Contains("text")) return "text";

        return normalized;
    }

    private static string? GetSubtitleBurnMethod(string? codecName)
    {
        var normalized = NormalizeSubtitleCodecName(codecName);
        if (normalized == null)
            return null;

        if (TextSubtitleCodecs.Contains(normalized)) return SubtitleBurnMethodText;
        if (BitmapSubtitleCodecs.Contains(normalized)) return SubtitleBurnMethodBitmap;
        return null;
    }

    private static string BuildAudioMapSpecifier(RtmpProbeStream? audio)
    {
        if (audio == null)
            return "0:a:0?";

        return audio.StreamIndex >= 0
            ? $"0:{audio.StreamIndex}"
            : $"0:a:{Math.Max(0, audio.AudioOrdinal)}?";
    }

    private static string DescribeStream(RtmpProbeStream stream)
    {
        var titleSegment = string.IsNullOrWhiteSpace(stream.Title) ? string.Empty : $" title={stream.Title}";
        var defaultSegment = stream.IsDefault ? " default=1" : string.Empty;
        var forcedSegment = stream.IsForced ? " forced=1" : string.Empty;
        return $"stream={stream.StreamIndex} lang={stream.Language} codec={stream.CodecName}{titleSegment}{defaultSegment}{forcedSegment}";
    }

    private static string BuildSubtitleFilter(string filePath, int subtitleOrdinal)
        => $"subtitles='{EscapeSubtitleFilterPath(filePath)}':si={subtitleOrdinal}";

    private static string EscapeSubtitleFilterPath(string path)
        => path
            .Replace("\\", "\\\\")
            .Replace(":", "\\:")
            .Replace("'", "\\'")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace(",", "\\,");

    private static string? ReadString(JsonNode? node, string propertyName)
        => ReadJsonValue(node?[propertyName]);

    private static string? ReadNestedString(JsonNode? node, string parentPropertyName, string propertyName)
        => ReadJsonValue(node?[parentPropertyName]?[propertyName]);

    private static int? ReadInt(JsonNode? node, string propertyName)
        => ReadJsonInt(node?[propertyName]);

    private static int? ReadNestedInt(JsonNode? node, string parentPropertyName, string propertyName)
        => ReadJsonInt(node?[parentPropertyName]?[propertyName]);

    private static string? ReadJsonValue(JsonNode? node)
    {
        if (node == null)
            return null;

        try
        {
            return node.GetValue<string>();
        }
        catch
        {
            return node.ToString();
        }
    }

    private static int? ReadJsonInt(JsonNode? node)
    {
        if (node == null)
            return null;

        try
        {
            return node.GetValue<int>();
        }
        catch
        {
            return int.TryParse(node.ToString(), out var value) ? value : null;
        }
    }

    private static string TrimForLog(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength] + "...";
    }

    private static void LogFfmpegProgress(RtmpFileShareSession session, IReadOnlyDictionary<string, string> progressFields)
    {
        var progressState = GetProgressValue(progressFields, "progress") ?? "unknown";
        var frame = ParseInt(progressFields, "frame");
        var fps = ParseDouble(progressFields, "fps");
        var speed = GetProgressValue(progressFields, "speed");
        var bitrate = GetProgressValue(progressFields, "bitrate");
        var dupFrames = ParseInt(progressFields, "dup_frames");
        var dropFrames = ParseInt(progressFields, "drop_frames");
        var outTimeSec = ParseOutTimeSeconds(progressFields);
        if (outTimeSec.HasValue)
        {
            session.LastEncoderOutTimeSec = Math.Max(session.LastEncoderOutTimeSec, outTimeSec.Value);
            session.LastEncoderProgressAtUtc = DateTime.UtcNow;
        }
        var elapsedSec = Math.Max(0, (DateTime.UtcNow - session.StartedAtUtc).TotalSeconds);
        var wallClockDriftSec = outTimeSec.HasValue ? elapsedSec - outTimeSec.Value : (double?)null;

        Console.WriteLine(
            $"{GetLogPrefix(session.RoomId)} ffmpeg progress={progressState} " +
            $"frame={(frame?.ToString() ?? "?")} fps={(fps?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) ?? "?")} " +
            $"speed={(speed ?? "?")} bitrate={(bitrate ?? "?")} " +
            $"outTimeSec={(outTimeSec?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "?")} " +
            $"wallClockDriftSec={(wallClockDriftSec?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "?")} " +
            $"dupFrames={(dupFrames?.ToString() ?? "?")} dropFrames={(dropFrames?.ToString() ?? "?")}");
    }

    private static string GetLogPrefix(string roomId)
        => $"[RtmpShare/{roomId[..Math.Min(8, roomId.Length)]}]";

    private static string? GetProgressValue(IReadOnlyDictionary<string, string> progressFields, string key)
        => progressFields.TryGetValue(key, out var value) ? value : null;

    private static int? ParseInt(IReadOnlyDictionary<string, string> progressFields, string key)
        => int.TryParse(GetProgressValue(progressFields, key), out var value) ? value : null;

    private static double? ParseDouble(IReadOnlyDictionary<string, string> progressFields, string key)
        => double.TryParse(
            GetProgressValue(progressFields, key),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    private static double? ParseOutTimeSeconds(IReadOnlyDictionary<string, string> progressFields)
    {
        var outTimeMs = GetProgressValue(progressFields, "out_time_ms");
        if (long.TryParse(outTimeMs, out var milliseconds))
            return milliseconds / 1_000_000d;

        var outTimeUs = GetProgressValue(progressFields, "out_time_us");
        if (long.TryParse(outTimeUs, out var microseconds))
            return microseconds / 1_000_000d;

        var outTime = GetProgressValue(progressFields, "out_time");
        if (TimeSpan.TryParse(outTime, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed.TotalSeconds;

        return null;
    }

    private sealed class RtmpPlaybackSelection
    {
        public bool ProbeSucceeded { get; init; }
        public string AudioMapSpecifier { get; init; } = "0:a:0?";
        public string AudioSelectionLabel { get; init; } = "first audio fallback";
        public string? SubtitleFilter { get; init; }
        public int? SubtitleOrdinal { get; init; }
        public string? SubtitleBurnMethod { get; init; }
        public string? SubtitleSelectionLabel { get; init; }
    }

    private sealed class RtmpProbeStream
    {
        public int StreamIndex { get; init; }
        public int AudioOrdinal { get; set; } = -1;
        public int SubtitleOrdinal { get; set; } = -1;
        public string CodecType { get; init; } = string.Empty;
        public string CodecName { get; init; } = string.Empty;
        public string Language { get; init; } = "und";
        public string Title { get; init; } = string.Empty;
        public bool IsDefault { get; init; }
        public bool IsForced { get; init; }
    }

    // ── Position broadcast timer ──────────────────────────────────────────────

    private void OnPositionTick(object? _)
    {
        foreach (var (roomId, session) in _sessions)
        {
            if (session.Paused) continue;
            var pos = session.CurrentPositionSec;
            _ = _hub.Clients.Group(roomId).rtmpFileSharePosition(pos);
        }
    }

    public void Dispose()
    {
        _positionTimer.Dispose();
        foreach (var session in _sessions.Values)
            KillFfmpeg(session);
        _sessions.Clear();
    }
}
