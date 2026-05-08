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

namespace SyncStreamAPI.Helper.Streaming;

/// <summary>
/// Manages the lifecycle of RTMP file-share sessions: spawning/killing ffmpeg processes,
/// tracking playback position, and broadcasting position ticks to rooms via SignalR.
/// Registered as a singleton in DI.
/// </summary>
public class RtmpFileShareManager : IDisposable
{
    private static readonly HashSet<string> JapaneseAudioLanguages = new(StringComparer.OrdinalIgnoreCase)
        { "jpn", "ja", "jp" };
    private static readonly HashSet<string> EnglishLanguages = new(StringComparer.OrdinalIgnoreCase)
        { "eng", "en" };
    private static readonly HashSet<string> GermanLanguages = new(StringComparer.OrdinalIgnoreCase)
        { "deu", "ger", "de" };
    private static readonly HashSet<string> BurnableSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
        { "subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text" };

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
        string streamToken, string filePath, string? uploadId)
    {
        var playbackSelection = await ProbePlaybackSelectionAsync(filePath);

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
            AudioMapSpecifier = playbackSelection.AudioMapSpecifier,
            AudioSelectionLabel = playbackSelection.AudioSelectionLabel,
            SubtitleFilter = playbackSelection.SubtitleFilter,
            SubtitleSelectionLabel = playbackSelection.SubtitleSelectionLabel,
        };

        _sessions[roomId] = session;

        // Probe duration in the background — does not block stream start.
        _ = Task.Run(async () =>
        {
            try { session.DurationSec = await ProbeFileDurationAsync(filePath); }
            catch { /* probe failure is non-fatal */ }
        });

        SpawnFfmpeg(session, 0);
        return session;
    }

    /// <summary>Toggles play/pause for the session.</summary>
    public Task PausePlayAsync(string roomId, bool isPlaying)
    {
        if (!_sessions.TryGetValue(roomId, out var session)) return Task.CompletedTask;

        if (isPlaying && session.Paused)
        {
            session.Paused = false;
            session.RetryCount = 0; // explicit resume = fresh attempt
            SpawnFfmpeg(session, session.PositionSec);
        }
        else if (!isPlaying && !session.Paused)
        {
            session.Paused = true;
            RegisterExpectedPublisherDisconnect(session);
            KillFfmpeg(session);
        }
        return Task.CompletedTask;
    }

    /// <summary>Kills the current ffmpeg process and restarts from the new position.</summary>
    public Task SeekAsync(string roomId, double positionSec)
    {
        if (!_sessions.TryGetValue(roomId, out var session)) return Task.CompletedTask;
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
        KillFfmpeg(session);
        CleanUpUploadDir(session);
        return true;
    }

    /// <summary>Clears the upload-guard so seeking becomes available.</summary>
    public void MarkUploadComplete(string roomId, string uploadId)
    {
        if (_sessions.TryGetValue(roomId, out var session) && session.UploadId == uploadId)
            session.UploadId = null;
    }

    // ── ffmpeg process management ─────────────────────────────────────────────

    private void SpawnFfmpeg(RtmpFileShareSession session, double fromPositionSec)
    {
        var seekArg = fromPositionSec > 0.5
            ? $"-ss {fromPositionSec.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} "
            : string.Empty;
        var videoFilterArg = BuildVideoFilterArg(session);

        var rtmpUrl = $"{_rtmpBaseUrl}/{session.OwnerUsername}?token={Uri.EscapeDataString(session.StreamToken)}";

        var args = string.Concat(
            "-stats_period 1 -threads 0 ",
            $"-re {seekArg}",
            $"-i \"{session.FilePath}\" ",
            $"-map 0:v:0 -map {session.AudioMapSpecifier} ",
            videoFilterArg,
            $"-c:v libx264 -preset {_videoPreset} -tune zerolatency ",
            "-profile:v main -level:v 4.1 -pix_fmt yuv420p ",
            $"-b:v {_videoBitrateKbps}k -maxrate {_maxVideoBitrateKbps}k -bufsize {_videoBufferSizeKbps}k ",
            "-g 48 -keyint_min 48 ",
            "-x264-params scenecut=0:repeat-headers=1 ",
            $"-c:a aac -b:a {_audioBitrateKbps}k -ar 48000 -ac 2 ",
            $"-f flv \"{rtmpUrl}\""
        );

        Console.WriteLine(
            $"[RtmpShare/{session.RoomId.Substring(0, Math.Min(8, session.RoomId.Length))}] " +
            $"spawn ffmpeg seek={fromPositionSec:F3}s widthCap={_maxVideoWidth} preset={_videoPreset} " +
            $"video={_videoBitrateKbps}/{_maxVideoBitrateKbps}k audio={_audioBitrateKbps}k " +
            $"audioTrack=\"{session.AudioSelectionLabel}\" subtitleTrack=\"{session.SubtitleSelectionLabel ?? "none"}\"");

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

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"[RtmpShare/{session.RoomId.Substring(0, Math.Min(8, session.RoomId.Length))}] {e.Data}");
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
                Console.WriteLine($"[RtmpShare/{session.RoomId[..Math.Min(8, session.RoomId.Length)]}] " +
                                  $"ffmpeg exited after {elapsed:F1}s (retry {session.RetryCount}/{maxRetries}, next in {delaySec}s)");
                Task.Run(async () =>
                {
                    await Task.Delay(delaySec * 1000);
                    if (_sessions.ContainsKey(session.RoomId) && !session.Paused && session.Process == null)
                        SpawnFfmpeg(session, session.PositionSec);
                });
            }
            else
            {
                Console.WriteLine($"[RtmpShare/{session.RoomId[..Math.Min(8, session.RoomId.Length)]}] " +
                                  $"ffmpeg exited after {elapsed:F1}s and exhausted {maxRetries} retries — giving up");
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
            session.PendingPublisherDisconnects++;
    }

    private string BuildVideoFilterArg(RtmpFileShareSession session)
    {
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(session.SubtitleFilter))
            filters.Add(session.SubtitleFilter);

        if (_maxVideoWidth > 0)
            filters.Add($"scale=w='min(iw,{_maxVideoWidth})':h=-2");

        return filters.Count == 0 ? string.Empty : $"-vf \"{string.Join(",", filters)}\" ";
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

    private async Task<RtmpPlaybackSelection> ProbePlaybackSelectionAsync(string filePath)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = General.GetFFprobePath(),
                    Arguments = $"-v quiet -print_format json -show_streams \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            p.Start();
            var stdout = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await p.WaitForExitAsync().ConfigureAwait(false);

            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return new RtmpPlaybackSelection();

            var streams = JsonNode.Parse(stdout)?["streams"]?.AsArray();
            if (streams == null || streams.Count == 0)
                return new RtmpPlaybackSelection();

            var parsedStreams = new List<RtmpProbeStream>();
            var subtitleOrdinal = 0;

            foreach (var stream in streams)
            {
                if (stream == null) continue;

                var codecType = stream["codec_type"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(codecType)) continue;

                var parsed = new RtmpProbeStream
                {
                    StreamIndex = stream["index"]?.GetValue<int>() ?? -1,
                    CodecType = codecType,
                    CodecName = stream["codec_name"]?.GetValue<string>() ?? string.Empty,
                    Language = NormalizeLanguage(stream["tags"]?["language"]?.GetValue<string>()),
                    Title = stream["tags"]?["title"]?.GetValue<string>() ?? string.Empty,
                    IsDefault = (stream["disposition"]?["default"]?.GetValue<int>() ?? 0) == 1,
                    IsForced = (stream["disposition"]?["forced"]?.GetValue<int>() ?? 0) == 1,
                };

                if (string.Equals(codecType, "subtitle", StringComparison.OrdinalIgnoreCase))
                    parsed.SubtitleOrdinal = subtitleOrdinal++;

                parsedStreams.Add(parsed);
            }

            var subtitle = SelectSubtitleTrack(parsedStreams);
            var preferJapaneseWithSubtitles = subtitle != null;
            var audio = SelectAudioTrack(parsedStreams, preferJapaneseWithSubtitles);

            return new RtmpPlaybackSelection
            {
                AudioMapSpecifier = audio != null ? $"0:{audio.StreamIndex}" : "0:a:0?",
                AudioSelectionLabel = audio != null ? DescribeStream(audio) : "first audio fallback",
                SubtitleFilter = subtitle != null ? BuildSubtitleFilter(filePath, subtitle.SubtitleOrdinal) : null,
                SubtitleSelectionLabel = subtitle != null ? DescribeStream(subtitle) : null,
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RtmpShare] ffprobe track selection failed: {ex.Message}");
            return new RtmpPlaybackSelection();
        }
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

    private static RtmpProbeStream? SelectAudioTrack(IEnumerable<RtmpProbeStream> streams, bool preferJapaneseWithSubtitles)
    {
        var audioStreams = streams
            .Where(stream => string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase) && stream.StreamIndex >= 0)
            .ToList();

        if (audioStreams.Count == 0)
            return null;

        return audioStreams
            .OrderBy(stream => GetAudioLanguagePreferenceRank(stream.Language, preferJapaneseWithSubtitles))
            .ThenBy(stream => stream.IsDefault ? 0 : 1)
            .ThenBy(stream => stream.StreamIndex)
            .FirstOrDefault();
    }

    private static RtmpProbeStream? SelectSubtitleTrack(IEnumerable<RtmpProbeStream> streams)
    {
        var subtitleStreams = streams
            .Where(stream =>
                string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase) &&
                stream.StreamIndex >= 0 &&
                BurnableSubtitleCodecs.Contains(stream.CodecName))
            .ToList();

        if (subtitleStreams.Count == 0)
            return null;

        return subtitleStreams
            .OrderBy(stream => GetSubtitleLanguagePreferenceRank(stream.Language))
            .ThenBy(stream => stream.IsDefault ? 0 : 1)
            .ThenBy(stream => stream.IsForced ? 1 : 0)
            .ThenBy(stream => stream.StreamIndex)
            .FirstOrDefault();
    }

    private static int GetAudioLanguagePreferenceRank(string language, bool preferJapaneseWithSubtitles)
    {
        if (preferJapaneseWithSubtitles && JapaneseAudioLanguages.Contains(language)) return 0;
        if (EnglishLanguages.Contains(language)) return preferJapaneseWithSubtitles ? 1 : 0;
        if (GermanLanguages.Contains(language)) return preferJapaneseWithSubtitles ? 2 : 1;
        if (JapaneseAudioLanguages.Contains(language)) return preferJapaneseWithSubtitles ? 0 : 2;
        return 3;
    }

    private static int GetSubtitleLanguagePreferenceRank(string language)
    {
        if (EnglishLanguages.Contains(language)) return 0;
        if (GermanLanguages.Contains(language)) return 1;
        return 2;
    }

    private static string NormalizeLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) ? "und" : language.Trim().ToLowerInvariant();

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

    private sealed class RtmpPlaybackSelection
    {
        public string AudioMapSpecifier { get; init; } = "0:a:0?";
        public string AudioSelectionLabel { get; init; } = "first audio fallback";
        public string? SubtitleFilter { get; init; }
        public string? SubtitleSelectionLabel { get; init; }
    }

    private sealed class RtmpProbeStream
    {
        public int StreamIndex { get; init; }
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
