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

// ISO timestamp prefix for all log lines so events can be correlated.
const ts = () => new Date().toISOString().slice(11, 23); // HH:MM:SS.mmm

const FFMPEG_PATH = process.env.FFMPEG_PATH ??
  (os.platform() === 'win32' ? path.join(__dirname, '..', 'SyncStreamAPI', 'ffmpeg.exe') : 'ffmpeg');

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

/** State for one active server-side file stream in a room. */
class ServerFileStream {
  constructor({ videoTransport, audioTransport, videoProducer, audioProducer,
                videoPort, audioPort, videoLocalPort, audioLocalPort, filePath, targetBitrate }) {
    this.videoTransport = videoTransport;
    this.audioTransport = audioTransport;
    this.videoProducer  = videoProducer;
    this.audioProducer  = audioProducer;
    this.videoPort      = videoPort;       // mediasoup's RTP listening port (ffmpeg sends TO this)
    this.audioPort      = audioPort;
    this.videoLocalPort = videoLocalPort;  // fixed port ffmpeg binds FROM (stays constant on restart)
    this.audioLocalPort = audioLocalPort;
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
                       videoLocalPort, audioLocalPort,
                       videoPayloadType, audioPayloadType, videoSsrc, audioSsrc }) {
  const bitrateK = Math.round(targetBitrate / 1000);
  const seekArgs = startSec > 0.5 ? ['-ss', String(startSec.toFixed(3))] : [];

  const args = [
    ...seekArgs,
    '-stats_period', '1',
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
    '-f', 'rtp', `rtp://127.0.0.1:${videoPort}?pkt_size=1200&localport=${videoLocalPort}`,
    // Audio: transcode to Opus
    '-map', '0:a:0',
    '-c:a', 'libopus',
    '-b:a', '128k',
    '-ar', '48000',
    '-ac', '2',
    '-ssrc', String(audioSsrc),
    '-payload_type', String(audioPayloadType),
    '-f', 'rtp', `rtp://127.0.0.1:${audioPort}?pkt_size=1200&localport=${audioLocalPort}`,
  ];

  const proc = spawn(FFMPEG_PATH, args, {stdio: ['ignore', 'ignore', 'pipe']});
  console.log(`[ffmpeg|${proc.pid}] ${ts()} spawned startSec=${startSec} vPort=${videoPort} aPort=${audioPort}`);

  // Log ALL stderr lines for the first 30 (startup + first keyframe), then errors only.
  let stderrCount = 0;
  proc.stderr.on('data', (d) => {
    for (const raw of d.toString().split(/[\r\n]+/)) {
      const line = raw.trim();
      if (!line) continue;
      stderrCount++;
      const isProgressLine = line.includes('frame=') || line.includes('fps=') || line.includes('speed=');
      if (stderrCount <= 30 || isProgressLine || line.includes('rror') || line.includes('ail') || line.includes('nvalid')) {
        console.error(`[ffmpeg|${proc.pid}] ${line}`);
      }
    }
  });
  return proc;
}

/**
 * Creates a PlainTransport + Producer on the given mediasoup router for a single stream.
 * Returns { transport, producer, port, payloadType, ssrc }.
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
    rtcpMux:     true,
    comedia:     false,
  });

  // Set the remote address mediasoup will accept RTP from (and send RTCP to).
  await transport.connect({ ip: '127.0.0.1', port: localPort });

  // transport.tuple.localPort is mediasoup's UDP listening port (in the RTC
  // port range).  This is what ffmpeg must TARGET with its rtp:// URL.
  const port = transport.tuple.localPort;
  console.log(`[server-stream] ${ts()} ${kind} transport: mediasoup=${port} ffmpegSrc=${localPort}`);

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

  return {transport, producer, port, localPort, payloadType, ssrc};
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
    videoLocalPort: vResult.localPort,
    audioLocalPort: aResult.localPort,
    filePath,
    targetBitrate,
  });

  // Register producers in the room so consumers can subscribe.
  const serverPeerId = `server-file:${room.id}`;
  room.producers.set(vResult.producer.id, {producer: vResult.producer, peerId: serverPeerId});
  room.producers.set(aResult.producer.id, {producer: aResult.producer, peerId: serverPeerId});

  vResult.producer.on('transportclose', () => room.producers.delete(vResult.producer.id));
  aResult.producer.on('transportclose', () => room.producers.delete(aResult.producer.id));

  // Score events: score 0 means no RTP is reaching the producer from ffmpeg.
  vResult.producer.on('score', scores =>
    console.log(`[server-stream] ${ts()} video producer score: ${JSON.stringify(scores)}`));
  aResult.producer.on('score', scores =>
    console.log(`[server-stream] ${ts()} audio producer score: ${JSON.stringify(scores)}`));

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
    videoLocalPort:   vResult.localPort,
    audioLocalPort:   aResult.localPort,
    videoPayloadType: videoPt,
    audioPayloadType: audioPt,
    videoSsrc,
    audioSsrc,
  });

  stream.ffmpeg.on('exit', (code, signal) => {
    console.log(`[server-stream] ${ts()} ffmpeg exited code=${code} signal=${signal} room=${room.id}`);
  });

  console.log(`[server-stream] ${ts()} started room=${room.id} file=${filePath}`);
  return stream;
}

/**
 * Pauses the stream by killing ffmpeg and pausing the mediasoup producers.
 * Pausing producers signals all consumers to enter producer-paused state so
 * they get a clean reset when resume() is called.
 * @param {ServerFileStream} stream
 */
async function pauseServerFileStream(stream) {
  if (stream.paused) return;
  stream.positionSec = stream.currentPositionSec;
  stream.paused = true;
  stream.pausedAt = Date.now();
  stream.startedAt = 0;

  // Pause mediasoup producers BEFORE killing ffmpeg so consumers enter
  // producerPaused state cleanly.
  try { await stream.videoProducer.pause(); } catch (e) {
    console.error(`[server-stream] ${ts()} video producer.pause() failed: ${e.message}`);
  }
  try { await stream.audioProducer.pause(); } catch (e) {
    console.error(`[server-stream] ${ts()} audio producer.pause() failed: ${e.message}`);
  }

  if (stream.ffmpeg && !stream.ffmpeg.killed) {
    stream.ffmpeg.kill('SIGKILL');
    stream.ffmpeg = null;
  }
  console.log(`[server-stream] ${ts()} paused at ${stream.positionSec.toFixed(2)}s`);
}

/**
 * Resumes / seeks: kills any running ffmpeg, restarts at the target position,
 * then resumes the mediasoup producers so all consumers exit producer-paused
 * state with a clean RTP-sequence reset.
 * @param {ServerFileStream} stream
 * @param {number|null} seekToSec  - null = resume from pause position
 */
async function resumeServerFileStream(stream, seekToSec) {
  const isSeeking = seekToSec != null;
  stream.positionSec = isSeeking ? seekToSec : stream.positionSec;
  stream.paused = false;
  stream.startedAt = Date.now();

  // Kill any running ffmpeg first.
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
    videoLocalPort:   stream.videoLocalPort,
    audioLocalPort:   stream.audioLocalPort,
    videoPayloadType: 96,
    audioPayloadType: 97,
    videoSsrc:        stream.videoProducer.rtpParameters.encodings[0].ssrc,
    audioSsrc:        stream.audioProducer.rtpParameters.encodings[0].ssrc,
  });

  const newPid = stream.ffmpeg.pid;
  stream.ffmpeg.on('exit', (code, signal) => {
    if (!stream.paused) {
      console.log(`[server-stream] ${ts()} ffmpeg ended code=${code} signal=${signal} pid=${newPid}`);
    }
  });

  // Resume mediasoup producers. This signals every consumer to exit
  // producerPaused state with a fresh RTP-sequence-number window, so the
  // browser decoder receives the new keyframe and continues playing.
  try { await stream.videoProducer.resume(); } catch (e) {
    console.error(`[server-stream] ${ts()} video producer.resume() failed: ${e.message}`);
  }
  try { await stream.audioProducer.resume(); } catch (e) {
    console.error(`[server-stream] ${ts()} audio producer.resume() failed: ${e.message}`);
  }

  console.log(`[server-stream] ${ts()} resumed at ${stream.positionSec.toFixed(2)}s pid=${newPid}`);
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

  // Close transports and free the fixed ffmpeg source ports back to the allocator.
  if (stream.videoTransport && !stream.videoTransport.closed) {
    stream.videoTransport.close();
    freePort(stream.videoLocalPort);
  }
  if (stream.audioTransport && !stream.audioTransport.closed) {
    stream.audioTransport.close();
    freePort(stream.audioLocalPort);
  }

  console.log(`[server-stream] stopped room=${room.id}`);
}

module.exports = {startServerFileStream, pauseServerFileStream, resumeServerFileStream, stopServerFileStream};
