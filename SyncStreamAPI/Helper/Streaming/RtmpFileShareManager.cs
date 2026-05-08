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
            AudioMapSpecifier = playbackSelection.AudioMapSpecifier,
            AudioSelectionLabel = playbackSelection.AudioSelectionLabel,
            SubtitleFilter = playbackSelection.SubtitleFilter,
            SubtitleSelectionLabel = playbackSelection.SubtitleSelectionLabel,
        };

        _sessions[roomId] = session;

        // Probe duration in the background — does not block stream start.
        _ = Task.Run(async () =>
        {
            try
            {
                session.DurationSec = await ProbeFileDurationAsync(filePath);
                Console.WriteLine($"{GetLogPrefix(roomId)} durationProbe durationSec={(session.DurationSec?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")}");
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
        CleanUpUploadDir(session);
        return true;
    }

    /// <summary>Clears the upload-guard so seeking becomes available.</summary>
    public void MarkUploadComplete(string roomId, string uploadId)
    {
        if (_sessions.TryGetValue(roomId, out var session) && session.UploadId == uploadId)
        {
            session.UploadId = null;
            Console.WriteLine($"{GetLogPrefix(roomId)} upload completed uploadId={uploadId}");
        }
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
            "-nostats -progress pipe:1 -stats_period 1 -threads 0 ",
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
            $"{GetLogPrefix(session.RoomId)} spawn ffmpeg seek={fromPositionSec:F3}s widthCap={_maxVideoWidth} preset={_videoPreset} " +
            $"video={_videoBitrateKbps}/{_maxVideoBitrateKbps}k audio={_audioBitrateKbps}k " +
            $"audioTrack=\"{session.AudioSelectionLabel}\" subtitleTrack=\"{session.SubtitleSelectionLabel ?? "none"}\"");

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
                return new RtmpPlaybackSelection();
            }

            var streams = JsonNode.Parse(stdout)?["streams"]?.AsArray();
            if (streams == null || streams.Count == 0)
            {
                Console.WriteLine("[RtmpShare/Probe] ffprobe returned no streams");
                return new RtmpPlaybackSelection();
            }

            var parsedStreams = new List<RtmpProbeStream>();
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

                if (string.Equals(codecType, "subtitle", StringComparison.OrdinalIgnoreCase))
                    parsed.SubtitleOrdinal = subtitleOrdinal++;

                parsedStreams.Add(parsed);
            }

            var preferredSubtitle = SelectSubtitleTrack(parsedStreams);
            var audio = SelectAudioTrack(parsedStreams, playbackPreferences, preferredSubtitle != null);
            var subtitle = ResolveSubtitleTrack(preferredSubtitle, audio, playbackPreferences);

            Console.WriteLine(
                $"[RtmpShare/Probe] subtitleMode={playbackPreferences.SubtitleMode} " +
                $"audioPriority=[{string.Join(",", playbackPreferences.AudioLanguagePriority)}] " +
                $"audioCandidates=[{string.Join(" | ", parsedStreams.Where(s => string.Equals(s.CodecType, "audio", StringComparison.OrdinalIgnoreCase)).Select(DescribeStream))}] " +
                $"subtitleCandidates=[{string.Join(" | ", parsedStreams.Where(s => string.Equals(s.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase)).Select(DescribeStream))}] " +
                $"selectedAudio=\"{(audio != null ? DescribeStream(audio) : "first audio fallback")}\" " +
                $"selectedSubtitle=\"{(subtitle != null ? DescribeStream(subtitle) : "none")}\"");

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

    private static RtmpProbeStream? SelectAudioTrack(
        IEnumerable<RtmpProbeStream> streams,
        RtmpPlaybackPreferences playbackPreferences,
        bool subtitleAvailable)
    {
        var audioStreams = streams
            .Where(stream => string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase) && stream.StreamIndex >= 0)
            .ToList();

        if (audioStreams.Count == 0)
            return null;

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
        };
    }

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
