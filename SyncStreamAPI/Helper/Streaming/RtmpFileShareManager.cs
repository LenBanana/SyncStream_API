using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models.RTMP;

namespace SyncStreamAPI.Helper.Streaming;

/// <summary>A share that cannot start, in words the sharer can be shown.</summary>
public class RtmpFileShareException(string message) : Exception(message);

/// <summary>
///     Runs the ffmpeg behind each RTMP file share: starting, pausing, seeking, waiting for an upload that
///     playback caught up with, and retrying a run that failed. Rooms and clients are not its business; the hub
///     and <see cref="SyncStreamAPI.ServerData.Background.ServerFileShareMonitor" /> read its sessions' status.
/// </summary>
/// <remarks>
///     Every ffmpeg exit used to be treated as a crash and restarted at the position of the last pause or seek,
///     up to eight times. So a file that played to its end started over from the beginning, and a run that
///     caught up with a still-uploading file did the same.
/// </remarks>
public class RtmpFileShareManager : IDisposable
{
    private const string SubtitleBurnMethodText = "text";
    private const string SubtitleBurnMethodBitmap = "bitmap";
    private const string StreamNamePrefix = "share-";

    /// <summary>Failed runs in a row, once the whole file is there, before the share is given up.</summary>
    private const int MaxConsecutiveFailures = 3;

    /// <summary>A run that streamed this long is not part of a series of failures, whatever ended it.</summary>
    private const double SuccessfulRunSec = 10;

    /// <summary>
    ///     How much more of the upload to wait for after playback caught up with it: at least this many bytes, and
    ///     at least <see cref="RebufferContentSec" /> of playback when the file's bitrate is known. Starting again
    ///     on every few bytes would restart ffmpeg, and every viewer's player with it, every few seconds.
    /// </summary>
    private const long MinimumRebufferBytes = 8L * 1024 * 1024;

    private const double RebufferContentSec = 15;
    private static readonly TimeSpan UploadStallLimit = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DurationProbeTimeout = TimeSpan.FromSeconds(15);

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
    private readonly ConcurrentDictionary<string, RtmpFileShareSession> _sessions = new();
    private readonly int _maxVideoWidth;
    private readonly int _videoBitrateKbps;
    private readonly int _maxVideoBitrateKbps;
    private readonly int _videoBufferSizeKbps;
    private readonly int _audioBitrateKbps;
    private readonly string _videoPreset;

    public RtmpFileShareManager(IConfiguration configuration)
    {
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
    }

    /// <summary>Whether a stream name on the streaming server belongs to a file share rather than to OBS.</summary>
    public static bool IsShareStream(string? streamName)
        => streamName?.StartsWith(StreamNamePrefix, StringComparison.Ordinal) == true;

    public RtmpFileShareSession? Get(string roomId)
        => _sessions.TryGetValue(roomId, out var session) ? session : null;

    public RtmpFileShareSession? FindByStream(string streamName)
        => _sessions.Values.FirstOrDefault(session => session.StreamName == streamName);

    /// <summary>
    ///     Probes the file, picks its tracks and sets up the room's share, ready for <see cref="Begin" />. The two
    ///     are separate so the room can be told about the share before its first run can announce itself.
    /// </summary>
    /// <exception cref="RtmpFileShareException">The file cannot be shared, or the room already shares one.</exception>
    public async Task<RtmpFileShareSession> PrepareAsync(string roomId, string filePath, string uploadId,
        long totalBytes, bool uploadComplete, RtmpPlaybackPreferences? playbackPreferences)
    {
        var preferences = NormalizePlaybackPreferences(playbackPreferences);
        Console.WriteLine(
            $"{GetLogPrefix(roomId)} start requested file=\"{Path.GetFileName(filePath)}\" totalBytes={totalBytes} " +
            $"uploadComplete={uploadComplete} subtitleMode={preferences.SubtitleMode} " +
            $"audioPriority=[{string.Join(",", preferences.AudioLanguagePriority)}]");

        var selectionProbe = ProbePlaybackSelectionAsync(filePath, preferences);
        var durationProbe = ProbeFileDurationAsync(filePath);
        var selection = await selectionProbe;
        var duration = await durationProbe;
        if (selection.ProbeSucceeded && !selection.HasVideo)
            throw new RtmpFileShareException("This file has no video to stream.");

        var session = new RtmpFileShareSession
        {
            RoomId = roomId,
            StreamName = StreamNamePrefix + Guid.NewGuid().ToString("N"),
            PublishKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
            FilePath = filePath,
            UploadId = uploadId,
            TotalBytes = totalBytes,
            UploadComplete = uploadComplete,
            DurationSec = duration,
            PlaybackPreferences = preferences,
            State = RtmpFileShareState.Playing,
        };
        ApplyPlaybackSelection(session, selection);

        if (!_sessions.TryAdd(roomId, session))
            throw new RtmpFileShareException("A file is already being shared to this room.");

        return session;
    }

    /// <summary>Starts streaming a prepared share from the beginning.</summary>
    public void Begin(RtmpFileShareSession session) => Launch(session);

    /// <summary>Pauses a share that is playing or waiting for its upload. False when there was nothing to pause.</summary>
    public bool Pause(string roomId)
    {
        var session = Get(roomId);
        if (session == null) return false;

        lock (session.Gate)
        {
            if (session.State is not (RtmpFileShareState.Playing or RtmpFileShareState.Buffering)) return false;
            CancelWaiting(session);
            KillRun(session);
            session.State = RtmpFileShareState.Paused;
            Console.WriteLine($"{GetLogPrefix(roomId)} paused positionSec={session.PositionSec:F3}");
            return true;
        }
    }

    /// <summary>Resumes a paused share where it stopped. False when it was not paused.</summary>
    public bool Resume(string roomId)
    {
        var session = Get(roomId);
        if (session == null) return false;

        lock (session.Gate)
        {
            if (session.State != RtmpFileShareState.Paused) return false;
            session.State = RtmpFileShareState.Playing;
            session.ConsecutiveFailures = 0;
            Console.WriteLine($"{GetLogPrefix(roomId)} resumed positionSec={session.PositionSec:F3}");
        }

        Launch(session);
        return true;
    }

    /// <summary>
    ///     Moves the share to a position. A paused share stays paused there, as a player's would. False when the
    ///     share is over or unknown.
    /// </summary>
    public bool Seek(string roomId, double positionSec)
    {
        var session = Get(roomId);
        if (session == null) return false;

        bool relaunch;
        lock (session.Gate)
        {
            if (session.State is RtmpFileShareState.Ended or RtmpFileShareState.Failed) return false;
            CancelWaiting(session);
            KillRun(session);
            session.PositionSec = Math.Clamp(positionSec, 0, session.DurationSec ?? double.MaxValue);
            session.ConsecutiveFailures = 0;
            relaunch = session.State != RtmpFileShareState.Paused;
            if (relaunch) session.State = RtmpFileShareState.Playing;
            Console.WriteLine($"{GetLogPrefix(roomId)} seek positionSec={session.PositionSec:F3}");
        }

        if (relaunch) Launch(session);
        return true;
    }

    /// <summary>
    ///     Records that the whole file is there; a share waiting for it plays on straight away. A duration the
    ///     first part of the file did not give away is probed again now.
    /// </summary>
    public async Task MarkUploadCompleteAsync(string roomId)
    {
        var session = Get(roomId);
        if (session == null) return;

        var duration = session.DurationSec ?? await ProbeFileDurationAsync(session.FilePath);

        bool resume;
        lock (session.Gate)
        {
            if (session.UploadComplete) return;
            session.UploadComplete = true;
            session.DurationSec ??= duration;
            resume = session.State == RtmpFileShareState.Buffering;
            if (resume)
            {
                CancelWaiting(session);
                session.State = RtmpFileShareState.Playing;
            }
            Console.WriteLine($"{GetLogPrefix(roomId)} upload complete");
        }

        if (resume) Launch(session);
    }

    /// <summary>Ends the share and its ffmpeg. Returns the session that was stopped, or null when there was none.</summary>
    public RtmpFileShareSession? Stop(string roomId)
    {
        if (!_sessions.TryRemove(roomId, out var session)) return null;

        lock (session.Gate)
        {
            CancelWaiting(session);
            KillRun(session);
            Console.WriteLine($"{GetLogPrefix(roomId)} stopped state={session.State} positionSec={session.PositionSec:F3}");
        }

        return session;
    }

    /// <summary>
    ///     Checks a publish the streaming server asks about. Returns the share whose run is publishing, or null
    ///     when the stream or its key is not one of ours.
    /// </summary>
    public RtmpFileShareSession? AcceptPublisher(string streamName, string? key)
    {
        var session = FindPublisher(streamName, key);
        if (session == null) return null;

        lock (session.Gate)
        {
            // A run that was stopped while the streaming server was still asking about it is not live.
            session.IsPublisherLive = session.Process != null;
            return session.IsPublisherLive ? session : null;
        }
    }

    /// <summary>The streaming server reports that a share's run stopped publishing.</summary>
    public void PublisherGone(string streamName, string? key)
    {
        var session = FindPublisher(streamName, key);
        if (session == null) return;
        lock (session.Gate) session.IsPublisherLive = false;
    }

    private RtmpFileShareSession? FindPublisher(string streamName, string? key)
    {
        var session = FindByStream(streamName);
        return session != null && key != null &&
               CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(session.PublishKey))
            ? session
            : null;
    }

    // ── ffmpeg runs ───────────────────────────────────────────────────────────

    private bool IsCurrent(RtmpFileShareSession session)
        => _sessions.TryGetValue(session.RoomId, out var current) && ReferenceEquals(current, session);

    /// <summary>Starts a run at the session's position, unless the session has moved on in the meantime.</summary>
    private void Launch(RtmpFileShareSession session)
    {
        if (!session.PlaybackSelectionResolved) RefreshPlaybackSelectionIfNeeded(session);

        string rtmpBaseUrl;
        try
        {
            rtmpBaseUrl = ResolveRtmpBaseUrl();
        }
        catch (SocketException ex)
        {
            lock (session.Gate)
            {
                session.State = RtmpFileShareState.Failed;
                session.Error = "The streaming server could not be found.";
            }
            Console.WriteLine($"{GetLogPrefix(session.RoomId)} could not resolve {_rtmpBaseUrl}: {ex.Message}");
            return;
        }

        lock (session.Gate)
        {
            if (session.State != RtmpFileShareState.Playing || session.Process != null || !IsCurrent(session)) return;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = General.GetFFmpegPath(),
                    Arguments = BuildArguments(session, rtmpBaseUrl),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true
            };

            var progressFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            process.OutputDataReceived += (_, e) => OnProgressLine(session, process, progressFields, e.Data);
            process.ErrorDataReceived += (_, e) => OnErrorLine(session, process, e.Data);
            process.Exited += (_, _) => OnExited(session, process);

            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                process.Dispose();
                session.State = RtmpFileShareState.Failed;
                session.Error = "The server could not start ffmpeg.";
                Console.WriteLine($"{GetLogPrefix(session.RoomId)} ffmpeg could not be started: {ex}");
                return;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            session.Process = process;
            session.RunProgressSec = 0;
            session.RunProgressAtUtc = null;

            Console.WriteLine(
                $"{GetLogPrefix(session.RoomId)} ffmpeg started positionSec={session.PositionSec:F3} widthCap={_maxVideoWidth} " +
                $"preset={_videoPreset} video={_videoBitrateKbps}/{_maxVideoBitrateKbps}k audio={_audioBitrateKbps}k " +
                $"audioTrack=\"{session.AudioSelectionLabel}\" subtitleTrack=\"{session.SubtitleSelectionLabel ?? "none"}\" " +
                $"subtitleBurn={session.SubtitleBurnMethod ?? "none"} subtitles={SubtitleSource(session)}");
        }
    }

    /// <summary>
    ///     The streaming server's RTMP URL with its host resolved to an address. The ffmpeg in the API image is a
    ///     static build, and statically linked glibc cannot resolve host names: given one, ffmpeg crashes before
    ///     it connects. Production names the server by IP for that reason; resolving it here lets a host name,
    ///     such as a compose service, work too.
    /// </summary>
    /// <exception cref="SocketException">The host name does not resolve.</exception>
    private string ResolveRtmpBaseUrl()
    {
        var url = new UriBuilder(_rtmpBaseUrl);
        if (Uri.CheckHostName(url.Host) != UriHostNameType.Dns) return _rtmpBaseUrl;

        var addresses = Dns.GetHostAddresses(url.Host);
        var address = addresses.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork)
                      ?? addresses.First();
        url.Host = address.ToString();
        return url.Uri.ToString().TrimEnd('/');
    }

    /** Where burned-in text subtitles come from, for the log: the file the browser sent, or the media file itself. */
    private static string SubtitleSource(RtmpFileShareSession session)
    {
        if (!session.SubtitleOrdinal.HasValue ||
            !string.Equals(session.SubtitleBurnMethod, SubtitleBurnMethodText, StringComparison.OrdinalIgnoreCase))
            return "none";
        return SubtitleSidecars.SubtitleFor(session.UploadId, session.SubtitleOrdinal.Value) != null ? "sidecar" : "media";
    }

    private string BuildArguments(RtmpFileShareSession session, string rtmpBaseUrl)
    {
        var seekArg = session.PositionSec > 0.5
            ? $"-ss {session.PositionSec.ToString("F3", CultureInfo.InvariantCulture)} "
            : string.Empty;
        var rtmpUrl = $"{rtmpBaseUrl}/{session.StreamName}?token={session.PublishKey}";

        // No -tune zerolatency: a shared file gains nothing from shaving a second of latency, and that tuning
        // gives up B-frames, lookahead and frame threading, which cost quality per bit and encoding speed.
        // Keyframes every two seconds of content, whatever the frame rate, bound how long a joining viewer waits.
        return string.Concat(
            "-hide_banner -loglevel warning -nostdin -nostats -progress pipe:1 -stats_period 1 ",
            $"-re {seekArg}",
            $"-i \"{session.FilePath}\" ",
            BuildVideoMapAndFilterArg(session),
            $"-map {session.AudioMapSpecifier} ",
            $"-c:v libx264 -preset {_videoPreset} -profile:v high -pix_fmt yuv420p ",
            $"-b:v {_videoBitrateKbps}k -maxrate {_maxVideoBitrateKbps}k -bufsize {_videoBufferSizeKbps}k ",
            "-force_key_frames \"expr:gte(t,n_forced*2)\" -x264-params repeat-headers=1 ",
            $"-c:a aac -b:a {_audioBitrateKbps}k -ar 48000 -ac 2 ",
            "-max_muxing_queue_size 1024 ",
            $"-f flv \"{rtmpUrl}\"");
    }

    private static void OnProgressLine(RtmpFileShareSession session, Process process,
        Dictionary<string, string> fields, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var separator = line.IndexOf('=');
        if (separator <= 0) return;

        fields[line[..separator]] = line[(separator + 1)..];
        if (!line.StartsWith("progress=", StringComparison.OrdinalIgnoreCase)) return;

        var outTimeSec = ParseOutTimeSeconds(fields);
        fields.Clear();
        if (outTimeSec is not >= 0) return;

        lock (session.Gate)
        {
            if (!ReferenceEquals(session.Process, process)) return;
            session.RunProgressSec = Math.Max(session.RunProgressSec, outTimeSec.Value);
            session.RunProgressAtUtc = DateTime.UtcNow;
        }
    }

    private static void OnErrorLine(RtmpFileShareSession session, Process process, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Console.WriteLine($"{GetLogPrefix(session.RoomId)} ffmpeg: {line}");
        lock (session.Gate)
        {
            if (ReferenceEquals(session.Process, process)) session.LastFfmpegError = line.Trim();
        }
    }

    private void OnExited(RtmpFileShareSession session, Process process)
    {
        var exitCode = process.ExitCode;
        lock (session.Gate)
        {
            // A run that was stopped on purpose is no longer the session's run by the time it exits.
            if (ReferenceEquals(session.Process, process))
            {
                // Where ffmpeg itself got to, rather than the estimate between its reports, which can run up to a
                // second and a half ahead and would skip that much when the next run starts.
                var ranForSec = session.RunProgressSec;
                var reached = session.PositionSec + ranForSec;
                session.PositionSec = session.DurationSec is > 0 ? Math.Min(reached, session.DurationSec.Value) : reached;
                ClearRun(session);
                Console.WriteLine($"{GetLogPrefix(session.RoomId)} ffmpeg exited code={exitCode} ranForSec={ranForSec:F1} " +
                                  $"positionSec={session.PositionSec:F3} uploadComplete={session.UploadComplete}");
                if (session.State == RtmpFileShareState.Playing) AfterRunEnded(session, exitCode, ranForSec);
            }
        }

        process.Dispose();
    }

    /// <summary>Decides what a run that ended by itself means. Called under the session's lock.</summary>
    private void AfterRunEnded(RtmpFileShareSession session, int exitCode, double ranForSec)
    {
        // ffmpeg reads a growing file to the end of what has arrived and stops there, so with the upload still
        // going, any end is most likely playback catching up with it.
        if (!session.UploadComplete)
        {
            BeginBuffering(session);
            return;
        }

        if (exitCode == 0)
        {
            session.State = RtmpFileShareState.Ended;
            Console.WriteLine($"{GetLogPrefix(session.RoomId)} played to the end");
            return;
        }

        session.ConsecutiveFailures = ranForSec >= SuccessfulRunSec ? 1 : session.ConsecutiveFailures + 1;
        if (session.ConsecutiveFailures > MaxConsecutiveFailures)
        {
            session.State = RtmpFileShareState.Failed;
            session.Error = session.LastFfmpegError ?? $"ffmpeg stopped with exit code {exitCode}.";
            Console.WriteLine($"{GetLogPrefix(session.RoomId)} giving up after {MaxConsecutiveFailures} failed runs");
            return;
        }

        var delay = TimeSpan.FromSeconds(2 * session.ConsecutiveFailures);
        _ = RelaunchAfterAsync(session, delay, StartWaiting(session).Token);
    }

    /// <summary>Waits for enough of the upload to be worth starting again. Called under the session's lock.</summary>
    private void BeginBuffering(RtmpFileShareSession session)
    {
        session.State = RtmpFileShareState.Buffering;
        var bytesPerSec = session.DurationSec is > 0 ? session.TotalBytes / session.DurationSec.Value : 0;
        var target = FileLength(session.FilePath) + (long)Math.Max(MinimumRebufferBytes, bytesPerSec * RebufferContentSec);
        Console.WriteLine($"{GetLogPrefix(session.RoomId)} buffering until the upload reaches {target} bytes");
        _ = WaitForUploadAsync(session, target, StartWaiting(session).Token);
    }

    private async Task WaitForUploadAsync(RtmpFileShareSession session, long targetLength, CancellationToken token)
    {
        var lastLength = -1L;
        var lastGrowthUtc = DateTime.UtcNow;
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                var length = FileLength(session.FilePath);

                lock (session.Gate)
                {
                    if (token.IsCancellationRequested || session.State != RtmpFileShareState.Buffering) return;

                    if (!session.UploadComplete && length < targetLength)
                    {
                        if (length > lastLength)
                        {
                            lastLength = length;
                            lastGrowthUtc = DateTime.UtcNow;
                        }
                        else if (DateTime.UtcNow - lastGrowthUtc > UploadStallLimit)
                        {
                            session.Waiting = null;
                            session.State = RtmpFileShareState.Failed;
                            session.Error = "The upload stopped arriving, so the file could not be streamed any further.";
                            Console.WriteLine($"{GetLogPrefix(session.RoomId)} upload stalled at {length} bytes");
                        }

                        continue;
                    }

                    session.Waiting = null;
                    session.State = RtmpFileShareState.Playing;
                }

                Launch(session);
                return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Paused, seeked or stopped while waiting; whatever did that has taken over.
        }
    }

    private async Task RelaunchAfterAsync(RtmpFileShareSession session, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Paused, seeked or stopped before the retry; whatever did that has taken over.
            return;
        }

        lock (session.Gate)
        {
            if (token.IsCancellationRequested) return;
            session.Waiting = null;
        }

        Launch(session);
    }

    /// <summary>Replaces whatever the session was waiting on. Called under the session's lock.</summary>
    private static CancellationTokenSource StartWaiting(RtmpFileShareSession session)
    {
        CancelWaiting(session);
        session.Waiting = new CancellationTokenSource();
        return session.Waiting;
    }

    private static void CancelWaiting(RtmpFileShareSession session)
    {
        session.Waiting?.Cancel();
        session.Waiting = null;
    }

    /// <summary>Stops the current run, keeping its position. Called under the session's lock.</summary>
    private static void KillRun(RtmpFileShareSession session)
    {
        var process = session.Process;
        if (process == null) return;

        session.PositionSec = session.CurrentPositionSec;
        ClearRun(session);
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // It ended by itself a moment ago. Its exit handler finds it is no longer the session's run.
        }
    }

    private static void ClearRun(RtmpFileShareSession session)
    {
        session.Process = null;
        session.IsPublisherLive = false;
        session.RunProgressSec = 0;
        session.RunProgressAtUtc = null;
    }

    private static long FileLength(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? file.Length : 0;
    }

    private static int ParsePositiveInt(string? rawValue, int fallback)
        => int.TryParse(rawValue, out var value) && value > 0 ? value : fallback;

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
            string.Equals(session.SubtitleBurnMethod, SubtitleBurnMethodText, StringComparison.OrdinalIgnoreCase))
        {
            var sidecar = SubtitleSidecars.SubtitleFor(session.UploadId, session.SubtitleOrdinal.Value);
            var subtitleFilter = sidecar != null
                ? SubtitleSidecars.FilterForSidecar(sidecar, RoomUploadPaths.SidecarDirectoryFor(session.UploadId))
                : SubtitleSidecars.FilterForMedia(session.FilePath, session.SubtitleOrdinal.Value);

            // Seeking the input starts the frames' timestamps at zero, while the subtitles filter reads its
            // events from the file and places them by those timestamps. Without moving the frames back to where
            // they are in the file for the filter, a resumed or seeked share showed the subtitles of its first
            // minutes.
            var startedAt = session.PositionSec > 0.5
                ? session.PositionSec.ToString("F3", CultureInfo.InvariantCulture)
                : null;
            if (startedAt != null) filters.Add($"setpts=PTS+{startedAt}/TB");
            filters.Add(subtitleFilter);
            if (startedAt != null) filters.Add("setpts=PTS-STARTPTS");
        }

        if (!string.IsNullOrWhiteSpace(scaleFilter))
            filters.Add(scaleFilter);

        return filters.Count == 0
            ? "-map 0:v:0 "
            : $"-map 0:v:0 -vf \"{string.Join(",", filters)}\" ";
    }

    private void RefreshPlaybackSelectionIfNeeded(RtmpFileShareSession session)
    {
        if (session.PlaybackSelectionResolved)
            return;

        // Probing failed when the share started, most likely on too little of the file. A failed probe comes
        // back as the explicit fallback, which the session already has.
        var refreshedSelection = ProbePlaybackSelectionAsync(session.FilePath, session.PlaybackPreferences)
            .GetAwaiter()
            .GetResult();
        if (!refreshedSelection.ProbeSucceeded)
            return;

        lock (session.Gate)
            ApplyPlaybackSelection(session, refreshedSelection);
        Console.WriteLine(
            $"{GetLogPrefix(session.RoomId)} playback selection refreshed " +
            $"audioTrack=\"{session.AudioSelectionLabel}\" subtitleTrack=\"{session.SubtitleSelectionLabel ?? "none"}\"");
    }

    private static void ApplyPlaybackSelection(RtmpFileShareSession session, RtmpPlaybackSelection playbackSelection)
    {
        session.AudioMapSpecifier = playbackSelection.AudioMapSpecifier;
        session.AudioSelectionLabel = playbackSelection.AudioSelectionLabel;
        session.SubtitleOrdinal = playbackSelection.SubtitleOrdinal;
        session.SubtitleBurnMethod = playbackSelection.SubtitleBurnMethod;
        session.SubtitleSelectionLabel = playbackSelection.SubtitleSelectionLabel;
        session.PlaybackSelectionResolved = playbackSelection.ProbeSucceeded;
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
                HasVideo = parsedStreams.Any(s => string.Equals(s.CodecType, "video", StringComparison.OrdinalIgnoreCase)),
                AudioMapSpecifier = BuildAudioMapSpecifier(audio),
                AudioSelectionLabel = audio != null ? DescribeStream(audio) : "first audio fallback",
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
            SubtitleOrdinal = subtitleOrdinal,
            SubtitleBurnMethod = burnMethod,
            SubtitleSelectionLabel = $"client-selected subtitle ordinal={subtitleOrdinal} codec={playbackPreferences.SelectedSubtitleCodecName ?? "unknown"} ({reason})",
        };
    }

    private static async Task<double?> ProbeFileDurationAsync(string filePath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = General.GetFFprobePath(),
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(DurationProbeTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            Console.WriteLine($"[RtmpShare/Probe] duration probe timed out for \"{Path.GetFileName(filePath)}\"");
            return null;
        }

        var text = (await output).Trim();
        var stderr = await errors;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            return seconds;

        Console.WriteLine($"[RtmpShare/Probe] no duration for \"{Path.GetFileName(filePath)}\": {TrimForLog(stderr, 300)}");
        return null;
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

    private static string GetLogPrefix(string roomId)
        => $"[RtmpShare/{roomId[..Math.Min(8, roomId.Length)]}]";

    private static string? GetProgressValue(IReadOnlyDictionary<string, string> progressFields, string key)
        => progressFields.TryGetValue(key, out var value) ? value : null;

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
        /// <summary>Whether the file has a video stream. Assumed when the probe failed and nothing is known.</summary>
        public bool HasVideo { get; init; } = true;
        public string AudioMapSpecifier { get; init; } = "0:a:0?";
        public string AudioSelectionLabel { get; init; } = "first audio fallback";
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

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            lock (session.Gate)
            {
                CancelWaiting(session);
                KillRun(session);
            }
        }
        _sessions.Clear();
    }
}
