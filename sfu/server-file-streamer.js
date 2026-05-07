'use strict';

/**
 * Server-side file streaming via ffmpeg → mediasoup PlainTransport.
 *
 * Flow:
 *  1. POST /rooms/:roomId/server-stream  { filePath, targetBitrate? }
 *     → Creates two PlainTransports (video + audio), produces into the room,
 *       spawns ffmpeg -re -i <file> → RTP, returns producerIds.
 *  2. POST /rooms/:roomId/server-stream/control  { action: 'play'|'pause'|'seek', position? }
 *     → pause stops ffmpeg (or restarts with -ss for seek/play-after-pause).
 *  3. DELETE /rooms/:roomId/server-stream
 *     → Kills ffmpeg, closes PlainTransports + producers.
 *
 * Consumers see these producers exactly like peer producers — no client changes needed.
 * The host becomes a consumer of the same producers so they see the same picture as friends.
 */

const { spawn } = require('child_process');
const path = require('path');
const os   = require('os');

const FFMPEG_PATH = process.env.FFMPEG_PATH ??
  (os.platform() === 'win32' ? path.join(__dirname, '..', 'SyncStreamAPI', 'ffmpeg.exe') : 'ffmpeg');

// Shared port allocator used by recording.js too — avoid 40000-49999 range used by recording.
let _nextPort = 50000;
const _usedPorts = new Set();

function allocatePort() {
  const start = _nextPort;
  while (_usedPorts.has(_nextPort)) {
    _nextPort = (_nextPort >= 59999) ? 50000 : _nextPort + 1;
    if (_nextPort === start) throw new Error('No available streaming port');
  }
  const port = _nextPort;
  _nextPort = (port >= 59999) ? 50000 : port + 1;
  _usedPorts.add(port);
  return port;
}

function freePort(port) {
  _usedPorts.delete(port);
}

/** State for one active server-side file stream in a room. */
class ServerFileStream {
  constructor({ videoTransport, audioTransport, videoProducer, audioProducer,
                videoPort, audioPort, filePath, targetBitrate }) {
    this.videoTransport = videoTransport;
    this.audioTransport = audioTransport;
    this.videoProducer  = videoProducer;
    this.audioProducer  = audioProducer;
    this.videoPort      = videoPort;
    this.audioPort      = audioPort;
    this.filePath       = filePath;
    this.targetBitrate  = targetBitrate;
    this.ffmpeg         = null;
    this.positionSec    = 0;   // last known position (updated on pause/seek)
    this.startedAt      = 0;   // Date.now() when current ffmpeg started at positionSec
    this.pausedAt       = 0;   // Date.now() when stream was paused (for burst detection)
    this.paused         = false;
  }

  /** Current estimated playback position. */
  get currentPositionSec() {
    if (this.paused || !this.startedAt) return this.positionSec;
    return this.positionSec + (Date.now() - this.startedAt) / 1000;
  }

  get producerIds() {
    return {
      video: this.videoProducer?.id ?? null,
      audio: this.audioProducer?.id ?? null,
    };
  }
}

/**
 * Starts an ffmpeg process that sends RTP to the given ports.
 * Returns the ChildProcess.
 */
function spawnFfmpeg({ filePath, startSec, targetBitrate, videoPort, audioPort,
                       videoPayloadType, audioPayloadType, videoSsrc, audioSsrc }) {
  const bitrateK = Math.round(targetBitrate / 1000);
  const seekArgs = startSec > 0.5 ? ['-ss', String(startSec.toFixed(3))] : [];

  const args = [
    ...seekArgs,
    '-re',                        // real-time pacing
    '-i', filePath,
    // Video: transcode to VP8 real-time (VP8 is ~3× faster than VP9 to encode)
    '-map', '0:v:0',
    '-c:v', 'libvpx',
    '-deadline', 'realtime',
    '-cpu-used', '16',            // maximum speed for libvpx VP8
    '-threads', '4',
    '-b:v', `${bitrateK}k`,
    '-maxrate', `${bitrateK}k`,
    '-bufsize', `${bitrateK * 2}k`,
    '-keyint_min', '60',
    '-g', '60',
    '-pix_fmt', 'yuv420p',
    '-ssrc', String(videoSsrc),
    '-payload_type', String(videoPayloadType),
    '-f', 'rtp', `rtp://127.0.0.1:${videoPort}?pkt_size=1200`,
    // Audio: transcode to Opus
    '-map', '0:a:0',
    '-c:a', 'libopus',
    '-b:a', '128k',
    '-ar', '48000',
    '-ac', '2',
    '-ssrc', String(audioSsrc),
    '-payload_type', String(audioPayloadType),
    '-f', 'rtp', `rtp://127.0.0.1:${audioPort}?pkt_size=1200`,
  ];

  const proc = spawn(FFMPEG_PATH, args, {stdio: ['ignore', 'ignore', 'pipe']});
  proc.stderr.on('data', (d) => {
    const line = d.toString().trim();
    // Only log errors and frame progress at reduced rate.
    if (line.includes('Error') || line.includes('error')) console.error('[ffmpeg]', line);
  });
  return proc;
}

/**
 * Creates a PlainTransport + Producer on the given mediasoup router for a single stream.
 * Returns { transport, producer, port, payloadType, ssrc }.
 */
async function createPlainProducer(router, kind, payloadType, ssrc) {
  // comedia=true: mediasoup auto-detects ffmpeg's source address from the first
  // incoming RTP packet and keeps updating it on every packet — so a restarted
  // ffmpeg process (new ephemeral source port) is transparently accepted.
  const transport = await router.createPlainTransport({
    listenInfo: { protocol: 'udp', ip: '127.0.0.1' },
    rtcpMux:     true,
    comedia:     true,
  });

  // transport.tuple.localPort is mediasoup's UDP listening port (in the RTC
  // port range).  This is what ffmpeg must TARGET with its rtp:// URL.
  const port = transport.tuple.localPort;

  const rtpParameters = kind === 'video'
    ? {
        codecs: [{
          mimeType:    'video/VP8',
          payloadType,
          clockRate:   90000,
          parameters:  {},
        }],
        encodings: [{ssrc}],
      }
    : {
        codecs: [{
          mimeType:    'audio/opus',
          payloadType,
          clockRate:   48000,
          channels:    2,
          parameters:  {'sprop-stereo': 1},
        }],
        encodings: [{ssrc}],
      };

  const producer = await transport.produce({kind, rtpParameters, paused: false});

  return {transport, producer, port, payloadType, ssrc};
}

/**
 * Starts a new server-side file stream for the given room.
 * @param {object} room  - mediasoup Room object (has .router, .producers, .consumers)
 * @param {string} filePath
 * @param {number} targetBitrate  - bps for the VP9 video encoder
 * @param {function} onProducerCreated  - called with (producerId, kind, peerId) for each new producer
 * @returns {Promise<ServerFileStream>}
 */
async function startServerFileStream(room, filePath, targetBitrate, onProducerCreated) {
  // Fixed payload types outside the browser-registered range.
  const videoPt = 96, audioPt = 97;
  const videoSsrc = Math.floor(Math.random() * 0xFFFFFF) + 1;
  const audioSsrc = Math.floor(Math.random() * 0xFFFFFF) + 1;

  const [vResult, aResult] = await Promise.all([
    createPlainProducer(room.router, 'video', videoPt, videoSsrc),
    createPlainProducer(room.router, 'audio', audioPt, audioSsrc),
  ]);

  const stream = new ServerFileStream({
    videoTransport: vResult.transport,
    audioTransport: aResult.transport,
    videoProducer:  vResult.producer,
    audioProducer:  aResult.producer,
    videoPort:      vResult.port,
    audioPort:      aResult.port,
    filePath,
    targetBitrate,
  });

  // Register producers in the room so consumers can subscribe.
  const serverPeerId = `server-file:${room.id}`;
  room.producers.set(vResult.producer.id, {producer: vResult.producer, peerId: serverPeerId});
  room.producers.set(aResult.producer.id, {producer: aResult.producer, peerId: serverPeerId});

  vResult.producer.on('transportclose', () => room.producers.delete(vResult.producer.id));
  aResult.producer.on('transportclose', () => room.producers.delete(aResult.producer.id));

  // Notify the caller so it can signal clients via SignalR.
  onProducerCreated(vResult.producer.id, 'video', serverPeerId);
  onProducerCreated(aResult.producer.id, 'audio', serverPeerId);

  // Start ffmpeg.
  stream.startedAt = Date.now();
  stream.ffmpeg = spawnFfmpeg({
    filePath,
    startSec:         0,
    targetBitrate,
    videoPort:        vResult.port,
    audioPort:        aResult.port,
    videoPayloadType: videoPt,
    audioPayloadType: audioPt,
    videoSsrc,
    audioSsrc,
  });

  stream.ffmpeg.on('exit', (code) => {
    console.log(`[server-stream] ffmpeg exited code=${code} room=${room.id}`);
  });

  console.log(`[server-stream] started room=${room.id} file=${filePath}`);
  return stream;
}

/**
 * Pauses the stream by killing ffmpeg and recording the position.
 * @param {ServerFileStream} stream
 */
function pauseServerFileStream(stream) {
  if (stream.paused) return;
  stream.positionSec = stream.currentPositionSec;
  stream.paused = true;
  stream.pausedAt = Date.now();
  stream.startedAt = 0;
  if (stream.ffmpeg && !stream.ffmpeg.killed) {
    // SIGSTOP suspends without terminating: RTP packets stop flowing but sequence
    // numbers / SSRC / codec state are fully preserved.  SIGCONT later resumes
    // the same stream, so the browser decoder never needs to reset.
    try {
      process.kill(stream.ffmpeg.pid, 'SIGSTOP');
    } catch (e) {
      // Fallback: process already gone — treat as already paused.
      console.error('[server-stream] SIGSTOP failed:', e.message);
      stream.ffmpeg = null;
    }
  }
  console.log(`[server-stream] paused at ${stream.positionSec.toFixed(2)}s`);
}

/**
 * Resumes / seeks: restarts ffmpeg at the given position.
 * @param {ServerFileStream} stream
 * @param {number|null} seekToSec  - null = resume from pause position
 */
function resumeServerFileStream(stream, seekToSec) {
  const isSeeking = seekToSec != null;

  stream.positionSec = isSeeking ? seekToSec : stream.positionSec;
  stream.paused = false;
  stream.startedAt = Date.now();

  if (!isSeeking && stream.ffmpeg && !stream.ffmpeg.killed) {
    // Only SIGCONT for short pauses (< 3 s).  For longer pauses, -re pacing
    // would burst-send all the "owed" frames on wakeup, causing a fast-forward
    // artefact.  In that case restart at the paused position instead.
    const pauseDurationMs = Date.now() - stream.pausedAt;
    if (pauseDurationMs < 3000) {
      try {
        process.kill(stream.ffmpeg.pid, 'SIGCONT');
        console.log(`[server-stream] resumed at ${stream.positionSec.toFixed(2)}s`);
        return;
      } catch (e) {
        console.error('[server-stream] SIGCONT failed, restarting ffmpeg:', e.message);
        stream.ffmpeg = null;
      }
    } else {
      // Pause was long enough to cause a burst — kill and restart cleanly.
      stream.ffmpeg.kill('SIGKILL');
      stream.ffmpeg = null;
    }
  }

  // Seek (or fallback when process is gone): kill old ffmpeg and restart at new position.
  if (stream.ffmpeg && !stream.ffmpeg.killed) {
    stream.ffmpeg.kill('SIGKILL');
    stream.ffmpeg = null;
  }

  stream.ffmpeg = spawnFfmpeg({
    filePath:         stream.filePath,
    startSec:         stream.positionSec,
    targetBitrate:    stream.targetBitrate,
    videoPort:        stream.videoPort,
    audioPort:        stream.audioPort,
    videoPayloadType: 96,
    audioPayloadType: 97,
    videoSsrc:        stream.videoProducer.rtpParameters.encodings[0].ssrc,
    audioSsrc:        stream.audioProducer.rtpParameters.encodings[0].ssrc,
  });

  stream.ffmpeg.on('exit', (code) => {
    if (!stream.paused) {
      console.log(`[server-stream] ffmpeg ended code=${code}`);
    }
  });

  console.log(`[server-stream] resumed (seek) at ${stream.positionSec.toFixed(2)}s`);
}

/**
 * Stops and cleans up the server file stream completely.
 * @param {object} room
 * @param {ServerFileStream} stream
 */
function stopServerFileStream(room, stream) {
  if (stream.ffmpeg && !stream.ffmpeg.killed) {
    stream.ffmpeg.kill('SIGKILL');
    stream.ffmpeg = null;
  }

  // Close producers (triggers transportclose on consumers).
  if (stream.videoProducer && !stream.videoProducer.closed) {
    stream.videoProducer.close();
    room.producers.delete(stream.videoProducer.id);
  }
  if (stream.audioProducer && !stream.audioProducer.closed) {
    stream.audioProducer.close();
    room.producers.delete(stream.audioProducer.id);
  }

  // Close transports (mediasoup releases the RTC port back to its own pool).
  if (stream.videoTransport && !stream.videoTransport.closed) {
    stream.videoTransport.close();
  }
  if (stream.audioTransport && !stream.audioTransport.closed) {
    stream.audioTransport.close();
  }

  console.log(`[server-stream] stopped room=${room.id}`);
}

module.exports = {startServerFileStream, pauseServerFileStream, resumeServerFileStream, stopServerFileStream};
