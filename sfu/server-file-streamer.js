'use strict';

/**
 * Server-side file streaming via ffmpeg → mediasoup PlainTransport.
 *
 * Flow:
 *  1. POST /rooms/:roomId/server-stream  { filePath, targetBitrate?, growing? }
 *     → Probes the file, creates a PlainTransport + producer per stream, spawns
 *       ffmpeg -re -i <file> → RTP.
 *  2. POST /rooms/:roomId/server-stream/control
 *       { action: 'play' | 'pause' | 'seek' | 'upload-complete', position? }
 *  3. GET /rooms/:roomId/server-stream → state the API polls to learn when the file has finished.
 *  4. DELETE /rooms/:roomId/server-stream → kills ffmpeg, closes PlainTransports + producers.
 *
 * Consumers see these producers exactly like peer producers.
 */

const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const os = require('os');

// ISO timestamp prefix for all log lines so events can be correlated.
const ts = () => new Date().toISOString().slice(11, 23); // HH:MM:SS.mmm

const FFMPEG_PATH = process.env.FFMPEG_PATH ??
  (os.platform() === 'win32' ? path.join(__dirname, '..', 'SyncStreamAPI', 'ffmpeg.exe') : 'ffmpeg');
const FFPROBE_PATH = process.env.FFPROBE_PATH ??
  (os.platform() === 'win32' ? path.join(__dirname, '..', 'SyncStreamAPI', 'ffprobe.exe') : 'ffprobe');
const SERVER_STREAM_MAX_WIDTH = parseInt(process.env.SERVER_STREAM_MAX_WIDTH ?? '1920', 10);

// While the source is still being uploaded, playback can catch up with the upload. ffmpeg then stops at
// the end of the data and the stream rebuffers: it waits until this much more of the file has arrived (or
// the upload finishes) and restarts where it stopped. Restarting gives -re a fresh clock; keeping ffmpeg
// waiting on the file instead makes it burst through the backlog once data arrives, which viewers see as
// a fast-forward.
const REBUFFER_BYTES = 8 * 1024 * 1024;
const REBUFFER_POLL_MS = 1000;
// An uploader that adds nothing for this long has gone; the share fails rather than waiting forever.
const REBUFFER_GIVE_UP_MS = 120_000;
// ffmpeg reading a file that is still growing does not always exit at the end of the data; no progress
// for this long while growing counts as having reached it.
const STALL_MS = 2500;
// A run shorter than this counts as a failure to make progress; this many in a row ends the share.
const QUICK_EXIT_MS = 5_000;
const MAX_QUICK_EXITS = 8;
// A partial upload may not hold enough of the container for ffprobe yet.
const GROWING_PROBE_ATTEMPTS = 10;
const GROWING_PROBE_DELAY_MS = 1500;
const STDERR_TAIL_LINES = 12;

const VIDEO_PAYLOAD_TYPE = 96;
const AUDIO_PAYLOAD_TYPE = 97;

/** An error that maps onto an HTTP status for the control API. */
class StreamError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

// Port allocator for fixed ffmpeg source ports used with comedia=false.
// Range 20000-29999 is safely below the Linux ephemeral port range (32768-60999)
// so these ports will never be grabbed by the OS as ephemeral sockets.
//
// IMPORTANT: allocate in steps of 2.  ffmpeg's RTP muxer opens TWO sockets per
// stream: RTP on localport=N and RTCP on localport=N+1.  By only handing out
// even port numbers, each stream's auto-RTCP port (odd, N+1) never collides
// with another stream's RTP allocation.
let _nextPort = 20000;
const _usedPorts = new Set();

function allocatePort() {
  const start = _nextPort;
  while (_usedPorts.has(_nextPort)) {
    _nextPort = (_nextPort >= 29998) ? 20000 : _nextPort + 2;
    if (_nextPort === start) throw new Error('No available streaming port');
  }
  const port = _nextPort;
  _nextPort = (port >= 29998) ? 20000 : port + 2;
  _usedPorts.add(port);
  return port;
}

function freePort(port) {
  _usedPorts.delete(port);
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function normalizeLanguage(value) {
  return String(value ?? '').trim().toLowerCase();
}

function getLanguagePreferenceRank(language) {
  switch (language) {
    case 'jpn':
    case 'ja':
    case 'jp':
      return 0;
    case 'eng':
    case 'en':
      return 1;
    case 'deu':
    case 'ger':
    case 'de':
      return 2;
    default:
      return 3;
  }
}

function describeTrack(stream) {
  const language = normalizeLanguage(stream?.tags?.language) || 'und';
  const title = String(stream?.tags?.title ?? '').trim();
  const isDefault = Boolean(stream?.disposition?.default);
  return `stream=${stream?.index ?? '?'} lang=${language}${title ? ` title=${JSON.stringify(title)}` : ''}${isDefault ? ' default=1' : ''}`;
}

function choosePreferredAudioStream(audioStreams) {
  const ranked = [...audioStreams].sort((left, right) => {
    const leftRank = getLanguagePreferenceRank(normalizeLanguage(left.tags?.language));
    const rightRank = getLanguagePreferenceRank(normalizeLanguage(right.tags?.language));
    if (leftRank !== rightRank) return leftRank - rightRank;

    const leftDefault = left.disposition?.default ? 0 : 1;
    const rightDefault = right.disposition?.default ? 0 : 1;
    if (leftDefault !== rightDefault) return leftDefault - rightDefault;

    return (left.index ?? 0) - (right.index ?? 0);
  });
  return ranked[0];
}

function getVideoFilterArgs() {
  if (!Number.isFinite(SERVER_STREAM_MAX_WIDTH) || SERVER_STREAM_MAX_WIDTH <= 0) {
    return { filterArgs: [], scaleLabel: 'source-size' };
  }

  return {
    filterArgs: ['-vf', `scale=w=min(iw\\,${SERVER_STREAM_MAX_WIDTH}):h=-2`],
    scaleLabel: `maxWidth=${SERVER_STREAM_MAX_WIDTH}`,
  };
}

/** Runs ffprobe once and resolves with its parsed stream list, or rejects with what went wrong. */
function runProbe(filePath) {
  return new Promise((resolve, reject) => {
    const args = [
      '-v', 'error',
      '-print_format', 'json',
      '-show_entries', 'stream=index,codec_type,width,height:stream_tags=language,title:stream_disposition=default,forced',
      `file:${filePath}`,
    ];

    const proc = spawn(FFPROBE_PATH, args, { stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '';
    let stderr = '';
    proc.stdout.on('data', (chunk) => { stdout += chunk.toString(); });
    proc.stderr.on('data', (chunk) => { stderr += chunk.toString(); });
    proc.on('error', (err) => reject(new Error(`ffprobe could not be started: ${err.message}`)));
    proc.on('close', (code) => {
      if (code !== 0) {
        reject(new Error(stderr.trim() || `ffprobe exited with code ${code}`));
        return;
      }
      try {
        const json = JSON.parse(stdout);
        resolve(Array.isArray(json.streams) ? json.streams : []);
      } catch (err) {
        reject(new Error(`ffprobe printed unreadable JSON: ${err.message}`));
      }
    });
  });
}

/**
 * Decides what to stream. A file with no audio track gets no audio producer: mapping an audio stream
 * that is not there makes ffmpeg refuse to start at all.
 */
async function probeSourceSelection(filePath, growing) {
  const attempts = growing ? GROWING_PROBE_ATTEMPTS : 1;
  let lastError = null;

  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      const streams = await runProbe(filePath);
      const videoStream = streams.find((stream) => stream.codec_type === 'video');
      if (!videoStream) {
        throw new StreamError(422, 'The file has no video stream.');
      }
      const audioStreams = streams.filter((stream) => stream.codec_type === 'audio');
      const audio = audioStreams.length > 0 ? choosePreferredAudioStream(audioStreams) : null;
      return {
        audioMapSpecifier: audio ? `0:${audio.index}` : null,
        selectedAudioLabel: audio ? describeTrack(audio) : 'none (the file has no audio)',
        videoSourceLabel: `${videoStream.width ?? '?'}x${videoStream.height ?? '?'} stream=${videoStream.index ?? '?'}`,
      };
    } catch (err) {
      if (err instanceof StreamError) throw err;
      lastError = err;
      if (attempt < attempts) {
        console.warn(`[server-stream] ${ts()} probe attempt ${attempt}/${attempts} failed, waiting for more of the upload: ${err.message}`);
        await sleep(GROWING_PROBE_DELAY_MS);
      }
    }
  }

  throw new StreamError(422, `The file could not be read: ${lastError?.message ?? 'unknown error'}`);
}

/**
 * Creates a PlainTransport + Producer on the given mediasoup router for a single stream.
 */
async function createPlainProducer(router, kind, payloadType, ssrc) {
  // comedia=false + fixed ffmpeg source port:
  //   comedia=true locks the accepted source tuple after the FIRST packet, so a
  //   restarted ffmpeg (new ephemeral source port) is silently dropped.
  //   Instead we allocate a fixed port in 20000-29999 (below the Linux ephemeral
  //   range 32768-60999), tell mediasoup to accept only from that port via
  //   transport.connect(), and tell ffmpeg to bind that same port via ?localport=N.
  const localPort = allocatePort();

  const transport = await router.createPlainTransport({
    listenInfo: { protocol: 'udp', ip: '127.0.0.1' },
    rtcpMux: true,
    comedia: false,
  });

  try {
    await transport.connect({ ip: '127.0.0.1', port: localPort });

    const rtpParameters = kind === 'video'
      ? {
        // H.264 Constrained Baseline @ level 3.1 matches the second H.264 entry
        // in the router's mediaCodecs (server.js) and is the only H.264 profile
        // every WebRTC-capable browser is guaranteed to decode.  Picking Main
        // here would cause router.canConsume() to reject viewers whose device
        // caps don't include 4d0032 (common on Linux Chrome and mobile).
        // level-asymmetry-allowed=1 lets the actual bitstream level (5.0 for
        // 1080p output) exceed the signalled level (3.1) without renegotiation.
        codecs: [{
          mimeType: 'video/H264',
          payloadType,
          clockRate: 90000,
          parameters: {
            'packetization-mode': 1,
            'profile-level-id': '42e01f',
            'level-asymmetry-allowed': 1,
          },
          // Must mirror the router's mediaCodecs entry (server.js).  Even though
          // ffmpeg can't act on PLI/FIR (it doesn't read RTCP feedback), the
          // `nack` entry is what makes mediasoup synthesise the RTX pseudo-codec
          // for browser consumers: losing a single video packet between SFU and
          // browser then heals via retransmission instead of corrupting a frame.
          rtcpFeedback: [
            { type: 'nack' },
            { type: 'nack', parameter: 'pli' },
            { type: 'ccm', parameter: 'fir' },
            { type: 'goog-remb' },
            { type: 'transport-cc' },
          ],
        }],
        encodings: [{ ssrc }],
      }
      : {
        codecs: [{
          mimeType: 'audio/opus',
          payloadType,
          clockRate: 48000,
          channels: 2,
          parameters: { 'sprop-stereo': 1 },
        }],
        encodings: [{ ssrc }],
      };

    const producer = await transport.produce({ kind, rtpParameters, paused: false });
    // transport.tuple.localPort is mediasoup's UDP listening port; ffmpeg sends to it.
    return { transport, producer, port: transport.tuple.localPort, localPort, ssrc };
  } catch (err) {
    transport.close();
    freePort(localPort);
    throw err;
  }
}

/** State for one active server-side file stream in a room. */
class ServerFileStream {
  constructor({ roomId, filePath, targetBitrate, growing, selection, video, audio }) {
    this.roomId = roomId;
    this.filePath = filePath;
    this.targetBitrate = targetBitrate;
    // True while the file is still being uploaded and may end before the content does.
    this.growing = growing;
    this.selection = selection;
    this.video = video;
    this.audio = audio;

    /** @type {'playing' | 'paused' | 'ended' | 'failed' | 'stopped'} */
    this.state = 'playing';
    // True while playing but waiting for more of the upload before ffmpeg can carry on.
    this.buffering = false;
    this.error = null;
    this.ffmpeg = null;
    this.positionSec = 0;
    this.runStartSec = 0;
    this.runProgressSec = 0;
    this.runStartedAt = 0;
    this.runWhileGrowing = false;
    this.lastProgressAt = 0;
    this.quickExits = 0;
    this.stderrTail = [];
    this.stallWatchdog = null;
    this.rebufferTimer = null;
  }

  get currentPositionSec() {
    return this.ffmpeg ? this.runStartSec + this.runProgressSec : this.positionSec;
  }

  get producers() {
    return [this.video.producer, this.audio?.producer].filter(Boolean);
  }

  status() {
    return {
      state: this.state,
      buffering: this.buffering,
      positionSec: this.currentPositionSec,
      growing: this.growing,
      error: this.error,
      videoProducerId: this.video.producer.id,
      audioProducerId: this.audio?.producer.id ?? null,
    };
  }
}

function buildFfmpegArgs(stream, startSec) {
  const bitrateK = Math.round(stream.targetBitrate / 1000);
  const seekArgs = startSec > 0.5 ? ['-ss', startSec.toFixed(3)] : [];
  const { filterArgs } = getVideoFilterArgs();
  // Strict CBR.  Browser-side stats showed that any sustained bitrate above
  // ~3 Mbps over the SFU→browser leg triggered packet loss bursts (30-80
  // packets per 5s), which RTX could not heal fast enough and which surfaced
  // as multi-second decoder freezes.  We respect targetBitrate as a hard
  // ceiling: bufsize 2× maxrate is the loosest VBV that doesn't burst the
  // network, and a tighter setting hurts quality without helping loss.
  const bufferSizeK = bitrateK * 2;

  const args = [
    '-nostats',
    // Machine-readable progress on stdout: out_time_us is where playback actually is, which the
    // wall clock is not once the encoder falls behind or the upload makes ffmpeg wait.
    '-progress', 'pipe:1',
    '-stats_period', '0.5',
    ...seekArgs,
    // Decoder threading: 0 = auto (libavcodec picks based on cpu count). HEVC 10-bit
    // software decode is the heavy operation alongside encoding, so we let it use
    // every core; libx264's sliced-threads (enabled by zerolatency tune) shares
    // cores cooperatively rather than statically partitioning them.
    '-threads', '0',
    '-re',
    '-i', `file:${stream.filePath}`,

    // Video: libx264 with zerolatency tune.
    //
    // Why H.264 over VP8/VP9 here:
    //   * libx264 is dramatically faster than libvpx on the SFU's arm64 CPU,
    //     and that CPU is the bottleneck (ffmpeg speed was hovering 0.95-0.99x
    //     on libvpx, causing constant RTP underruns).
    //   * Quality at the same bitrate is on par with VP8 and visibly more
    //     consistent (less q-swing).
    //
    // Notes:
    //   * `repeat-headers=1` makes x264 emit SPS+PPS NALs before EVERY IDR.
    //     Required so consumers that join mid-stream can initialise their
    //     decoder on the next natural keyframe (every 60 frames ≈ 2.5 s).
    //   * `scenecut=0` disables scene-change keyframes so the GOP is a clean
    //     fixed cadence, predictable for RTP and bandwidth.
    //   * `-tune zerolatency` disables b-frames and lookahead (incompatible
    //     with sub-second streaming) and switches to slice-based threading
    //     so encoder latency is well below one frame.
    '-map', '0:v:0',
    ...filterArgs,
    '-c:v', 'libx264',
    '-preset', 'veryfast',
    '-tune', 'zerolatency',
    // Constrained Baseline is the H.264 profile every WebRTC-capable browser
    // is guaranteed to decode (it's mandatory in the spec).  Level 5.0 is
    // needed for 1080p; the SDP signals 3.1 with level-asymmetry-allowed=1 so
    // the actual SPS level can be higher.
    '-profile:v', 'baseline',
    '-level:v', '5.0',
    '-pix_fmt', 'yuv420p',
    '-b:v', `${bitrateK}k`,
    '-maxrate', `${bitrateK}k`,
    '-bufsize', `${bufferSizeK}k`,
    '-g', '60',
    '-keyint_min', '60',
    '-x264-params', 'scenecut=0:repeat-headers=1',
    '-ssrc', String(stream.video.ssrc),
    '-payload_type', String(VIDEO_PAYLOAD_TYPE),
    '-f', 'rtp', `rtp://127.0.0.1:${stream.video.port}?pkt_size=1200&localport=${stream.video.localPort}`,
  ];

  if (stream.audio) {
    args.push(
      '-map', stream.selection.audioMapSpecifier,
      '-c:a', 'libopus',
      '-b:a', '128k',
      '-ar', '48000',
      '-ac', '2',
      '-application', 'lowdelay',
      '-ssrc', String(stream.audio.ssrc),
      '-payload_type', String(AUDIO_PAYLOAD_TYPE),
      '-f', 'rtp', `rtp://127.0.0.1:${stream.audio.port}?pkt_size=1200&localport=${stream.audio.localPort}`,
    );
  }

  return args;
}

function rememberStderr(stream, line) {
  stream.stderrTail.push(line);
  if (stream.stderrTail.length > STDERR_TAIL_LINES) stream.stderrTail.shift();
}

function launchFfmpeg(stream, startSec) {
  const args = buildFfmpegArgs(stream, startSec);
  const proc = spawn(FFMPEG_PATH, args, { stdio: ['ignore', 'pipe', 'pipe'] });
  stream.ffmpeg = proc;
  stream.runStartSec = startSec;
  stream.runProgressSec = 0;
  stream.runStartedAt = Date.now();
  stream.runWhileGrowing = stream.growing;
  // Opening and seeking a large file can take a few seconds before the first frame comes out.
  stream.lastProgressAt = Date.now() + 5000;
  stream.stderrTail = [];

  if (stream.growing) {
    stream.stallWatchdog = setInterval(() => {
      if (stream.ffmpeg !== proc || !stream.growing) return;
      if (Date.now() - stream.lastProgressAt < STALL_MS) return;
      console.log(`[server-stream] ${ts()} caught up with the upload room=${stream.roomId} at ${stream.currentPositionSec.toFixed(2)}s`);
      stopFfmpeg(stream);
      startRebuffering(stream);
    }, 500);
  }

  const { scaleLabel } = getVideoFilterArgs();
  console.log(`[ffmpeg|${proc.pid}] ${ts()} spawned room=${stream.roomId} startSec=${startSec.toFixed(2)} growing=${stream.growing} ` +
    `source=${stream.selection.videoSourceLabel} scale=${scaleLabel} audio=${stream.selection.selectedAudioLabel} ` +
    `cbr=${Math.round(stream.targetBitrate / 1000)}k`);

  let pending = '';
  proc.stdout.on('data', (chunk) => {
    pending += chunk.toString();
    const lines = pending.split('\n');
    pending = lines.pop();
    for (const line of lines) {
      if (!line.startsWith('out_time_us=')) continue;
      const micros = Number(line.slice('out_time_us='.length));
      if (Number.isFinite(micros) && micros >= 0 && stream.ffmpeg === proc) {
        const seconds = micros / 1_000_000;
        if (seconds > stream.runProgressSec) stream.lastProgressAt = Date.now();
        stream.runProgressSec = seconds;
      }
    }
  });

  let stderrCount = 0;
  proc.stderr.on('data', (d) => {
    for (const raw of d.toString().split(/[\r\n]+/)) {
      const line = raw.trim();
      if (!line) continue;
      stderrCount++;
      rememberStderr(stream, line);
      // Startup lines and anything that looks like a problem; the progress stream carries the rest.
      if (stderrCount <= 30 || /rror|ail|nvalid/.test(line)) {
        console.error(`[ffmpeg|${proc.pid}] ${line}`);
      }
    }
  });

  proc.on('error', (err) => {
    rememberStderr(stream, `ffmpeg could not be started: ${err.message}`);
    console.error(`[ffmpeg] ${ts()} spawn failed room=${stream.roomId}: ${err.message}`);
  });
  proc.on('exit', (code, signal) => onFfmpegExit(stream, proc, code, signal));
}

/** Detaches and kills the running ffmpeg, keeping the position it had reached. */
function stopFfmpeg(stream) {
  clearInterval(stream.stallWatchdog);
  stream.stallWatchdog = null;
  clearTimeout(stream.rebufferTimer);
  stream.rebufferTimer = null;
  stream.buffering = false;
  const proc = stream.ffmpeg;
  if (!proc) return;
  stream.positionSec = stream.runStartSec + stream.runProgressSec;
  stream.ffmpeg = null;
  proc.kill('SIGKILL');
}

function fail(stream, error) {
  stream.state = 'failed';
  stream.buffering = false;
  stream.error = error;
  console.error(`[server-stream] ${ts()} failed room=${stream.roomId}: ${error}`);
}

function fileSize(filePath) {
  try {
    return fs.statSync(filePath).size;
  } catch (err) {
    if (err.code === 'ENOENT') return 0;
    throw err;
  }
}

function onFfmpegExit(stream, proc, code, signal) {
  // A process this module killed or replaced is no longer stream.ffmpeg.
  if (stream.ffmpeg !== proc) return;
  clearInterval(stream.stallWatchdog);
  stream.stallWatchdog = null;
  stream.ffmpeg = null;
  stream.positionSec = stream.runStartSec + stream.runProgressSec;
  if (stream.state !== 'playing') return;

  const ranMs = Date.now() - stream.runStartedAt;
  console.log(`[server-stream] ${ts()} ffmpeg exited room=${stream.roomId} code=${code} signal=${signal} ` +
    `ranMs=${ranMs} position=${stream.positionSec.toFixed(2)} growing=${stream.growing}`);

  // Only a run that played nothing counts as failing; a short run between rebuffers is progress.
  stream.quickExits = ranMs < QUICK_EXIT_MS && stream.runProgressSec < 1 ? stream.quickExits + 1 : 0;
  if (stream.quickExits >= MAX_QUICK_EXITS) {
    fail(stream, `ffmpeg kept stopping without playing anything: ${stream.stderrTail.slice(-3).join(' | ')}`);
    return;
  }

  if (stream.growing) {
    // The upload has not finished, so running out of data is not the end of the file.
    startRebuffering(stream);
    return;
  }

  if (stream.runWhileGrowing) {
    // This run started before the upload finished and may have stopped at what was the end of the data
    // then. A run started on the complete file decides whether this really is the end.
    restartAt(stream, stream.positionSec).catch(err => fail(stream, `Restart failed: ${err.message}`));
    return;
  }

  if (code === 0) {
    stream.state = 'ended';
    console.log(`[server-stream] ${ts()} reached the end room=${stream.roomId}`);
    return;
  }

  fail(stream, stream.stderrTail.slice(-3).join(' | ') || `ffmpeg exited with code ${code}`);
}

/** Waits until enough more of the upload has arrived, then carries on from the current position. */
function startRebuffering(stream) {
  stream.buffering = true;
  const sizeAtStall = fileSize(stream.filePath);
  // Bytes per second of content so far, to wait for roughly ten seconds' worth rather than a fixed amount.
  const bytesPerSecond = stream.positionSec > 1 ? sizeAtStall / stream.positionSec : 0;
  const target = sizeAtStall + Math.max(REBUFFER_BYTES, bytesPerSecond * 10);
  let lastSize = sizeAtStall;
  let lastGrowthAt = Date.now();

  console.log(`[server-stream] ${ts()} rebuffering room=${stream.roomId} at ${stream.positionSec.toFixed(2)}s ` +
    `size=${sizeAtStall} waitingFor=${Math.round(target)}`);

  const poll = () => {
    stream.rebufferTimer = null;
    if (stream.state !== 'playing' || stream.ffmpeg) return;

    const size = fileSize(stream.filePath);
    if (size > lastSize) {
      lastSize = size;
      lastGrowthAt = Date.now();
    }

    if (!stream.growing || size >= target) {
      stream.buffering = false;
      restartAt(stream, stream.positionSec).catch(err => fail(stream, `Restart failed: ${err.message}`));
      return;
    }
    if (Date.now() - lastGrowthAt > REBUFFER_GIVE_UP_MS) {
      fail(stream, 'The upload stopped arriving.');
      return;
    }
    stream.rebufferTimer = setTimeout(poll, REBUFFER_POLL_MS);
  };
  stream.rebufferTimer = setTimeout(poll, REBUFFER_POLL_MS);
}

async function setProducersPaused(stream, paused) {
  for (const producer of stream.producers) {
    if (producer.closed) continue;
    if (paused) await producer.pause();
    else await producer.resume();
  }
}

/**
 * Restarts ffmpeg at a position. Pausing the producers around the restart makes every consumer
 * resynchronise its sequence numbers and ask for a keyframe when they resume, so the new ffmpeg's
 * stream is picked up cleanly.
 */
async function restartAt(stream, positionSec) {
  await setProducersPaused(stream, true);
  stopFfmpeg(stream);
  stream.positionSec = Math.max(0, positionSec);
  stream.state = 'playing';
  stream.error = null;
  launchFfmpeg(stream, stream.positionSec);
  await setProducersPaused(stream, false);
}

/**
 * Starts a new server-side file stream in the given room and returns it. The caller registers
 * stream.producers with the room.
 */
async function startServerFileStream(room, { filePath, targetBitrate, growing }) {
  const selection = await probeSourceSelection(filePath, growing);

  const videoSsrc = Math.floor(Math.random() * 0xFFFFFF) + 1;
  const audioSsrc = Math.floor(Math.random() * 0xFFFFFF) + 1;

  const video = await createPlainProducer(room.router, 'video', VIDEO_PAYLOAD_TYPE, videoSsrc);
  let audio = null;
  if (selection.audioMapSpecifier) {
    try {
      audio = await createPlainProducer(room.router, 'audio', AUDIO_PAYLOAD_TYPE, audioSsrc);
    } catch (err) {
      video.transport.close();
      freePort(video.localPort);
      throw err;
    }
  }

  const stream = new ServerFileStream({ roomId: room.id, filePath, targetBitrate, growing, selection, video, audio });
  launchFfmpeg(stream, 0);
  console.log(`[server-stream] ${ts()} started room=${room.id} file=${filePath}`);
  return stream;
}

/** Stops ffmpeg and pauses the producers so consumers resynchronise cleanly on resume. */
async function pauseServerFileStream(stream) {
  if (stream.state !== 'playing') return;
  stream.state = 'paused';
  await setProducersPaused(stream, true);
  stopFfmpeg(stream);
  console.log(`[server-stream] ${ts()} paused room=${stream.roomId} at ${stream.positionSec.toFixed(2)}s`);
}

/**
 * Plays from the current position, or from positionSec when given. Also restarts a stream that ended
 * or failed, which is how the host replays or seeks back after the end.
 */
async function playServerFileStream(stream, positionSec) {
  if (stream.state === 'stopped') throw new StreamError(409, 'The stream has been stopped');
  if (stream.state === 'playing' && positionSec == null) return;

  stream.quickExits = 0;
  await restartAt(stream, positionSec ?? stream.positionSec);
  console.log(`[server-stream] ${ts()} playing room=${stream.roomId} from ${stream.positionSec.toFixed(2)}s`);
}

/** The upload has finished: from now on reaching the end of the file means the content is over. */
function markUploadComplete(stream) {
  stream.growing = false;
  console.log(`[server-stream] ${ts()} upload complete room=${stream.roomId}`);
}

/** Stops and cleans up the server file stream completely. */
function stopServerFileStream(stream) {
  stream.state = 'stopped';
  stopFfmpeg(stream);

  for (const part of [stream.video, stream.audio]) {
    if (!part) continue;
    if (!part.producer.closed) part.producer.close();
    if (!part.transport.closed) part.transport.close();
    freePort(part.localPort);
  }

  console.log(`[server-stream] ${ts()} stopped room=${stream.roomId}`);
}

module.exports = {
  StreamError,
  startServerFileStream,
  pauseServerFileStream,
  playServerFileStream,
  markUploadComplete,
  stopServerFileStream,
};
