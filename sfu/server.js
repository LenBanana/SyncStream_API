'use strict';

const express = require('express');
const mediasoup = require('mediasoup');
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const os = require('os');
const { RoomAbr } = require('./abr');
const {
  startServerFileStream,
  pauseServerFileStream,
  resumeServerFileStream,
  stopServerFileStream,
} = require('./server-file-streamer');

// ─── Configuration ────────────────────────────────────────────────────────────

const config = {
  httpPort: parseInt(process.env.SFU_HTTP_PORT ?? '3000', 10),

  // Public IP of the server (announced in ICE candidates so browsers can reach us).
  announcedIp: process.env.SFU_ANNOUNCED_IP ?? '152.53.224.14',

  // UDP port range for RTP/RTCP.  Must be opened in the firewall / Docker publish.
  rtcMinPort: parseInt(process.env.SFU_RTC_MIN_PORT ?? '10000', 10),
  rtcMaxPort: parseInt(process.env.SFU_RTC_MAX_PORT ?? '10099', 10),

  // Transport bitrate tuning.  This deployment prioritizes quality over density,
  // so we start transports with a much higher outgoing budget than mediasoup's
  // conservative defaults and optionally allow higher ingress from producers.
  initialAvailableOutgoingBitrate: parseInt(process.env.SFU_INITIAL_OUTGOING_BITRATE ?? '10000000', 10),
  maxIncomingBitrate: parseInt(process.env.SFU_MAX_INCOMING_BITRATE ?? '20000000', 10),

  // Number of mediasoup Worker processes.  Defaults to the number of logical CPU
  // cores so multiple rooms are spread across workers and no single thread becomes
  // the bottleneck.  Override with SFU_NUM_WORKERS env var.
  numWorkers: process.env.SFU_NUM_WORKERS
    ? parseInt(process.env.SFU_NUM_WORKERS, 10)
    : os.cpus().length,

  // Directory where recordings are written.  Mount this as a shared volume
  // so the .NET API can serve finished files.
  recordingsDir: process.env.SFU_RECORDINGS_DIR ?? '/recordings',

  // Media codecs supported by the router.
  mediaCodecs: [
    {
      kind: 'audio',
      mimeType: 'audio/opus',
      clockRate: 48000,
      channels: 2,
    },
    {
      kind: 'video',
      mimeType: 'video/VP8',
      clockRate: 90000,
      parameters: { 'x-google-start-bitrate': 1000 },
    },
    {
      kind: 'video',
      mimeType: 'video/VP9',
      clockRate: 90000,
      parameters: {
        'x-google-start-bitrate': 1000,
      },
    },
    {
      kind: 'video',
      mimeType: 'video/h264',
      clockRate: 90000,
      parameters: {
        'packetization-mode': 1,
        'profile-level-id': '4d0032',
        'level-asymmetry-allowed': 1,
        'x-google-start-bitrate': 1000,
      },
    },
    {
      kind: 'video',
      mimeType: 'video/h264',
      clockRate: 90000,
      parameters: {
        'packetization-mode': 1,
        'profile-level-id': '42e01f',
        'level-asymmetry-allowed': 1,
        'x-google-start-bitrate': 1000,
      },
    },
  ],
};

// ─── State ────────────────────────────────────────────────────────────────────

/** @type {mediasoup.types.Worker[]} */
const workers = [];
let nextWorkerIndex = 0;

/**
 * Active rooms.  Key = roomId (string).
 * @type {Map<string, Room>}
 */
const rooms = new Map();

/**
 * A single long-lived router used only for serving RTP capabilities.
 * All routers in this process share the same codecs, so one is sufficient.
 * @type {mediasoup.types.Router | null}
 */
let capabilitiesRouter = null;

class Room {
  /**
   * @param {string} id
   * @param {mediasoup.types.Router} router
   */
  constructor(id, router) {
    this.id = id;
    this.router = router;
    /** @type {Map<string, mediasoup.types.WebRtcTransport>} transportId → transport */
    this.transports = new Map();
    /** @type {Map<string, { producer: mediasoup.types.Producer, peerId: string }>} producerId → { producer, peerId } */
    this.producers = new Map();
    /** @type {Map<string, mediasoup.types.Consumer>} consumerId → consumer */
    this.consumers = new Map();
    // Recording state
    /** @type {{ process: import('child_process').ChildProcess, filename: string, startedAtUnixMs: number } | null} */
    this.recording = null;
    /** @type {RoomAbr | null} */
    this.abr = null;
    /** @type {import('./server-file-streamer').ServerFileStream | null} */
    this.serverFileStream = null;
  }

  toJSON() {
    return { id: this.id };
  }
}

// ─── Startup ─────────────────────────────────────────────────────────────────

async function startWorkers() {
  for (let i = 0; i < config.numWorkers; i++) {
    const worker = await mediasoup.createWorker({
      rtcMinPort: config.rtcMinPort,
      rtcMaxPort: config.rtcMaxPort,
      logLevel: 'warn',
      logTags: ['rtp', 'srtp', 'rtcp', 'rtx', 'bwe', 'score', 'simulcast', 'svc'],
    });

    worker.on('died', (err) => {
      console.error(`mediasoup Worker ${i} died:`, err);
      process.exit(1);
    });

    workers.push(worker);
    console.log(`mediasoup Worker ${i} pid=${worker.pid}`);
  }

  // Create the shared capabilities router on the first worker.
  capabilitiesRouter = await workers[0].createRouter({ mediaCodecs: config.mediaCodecs });
  console.log('Capabilities router ready');
}

/** Round-robin worker selection. */
function getNextWorker() {
  const worker = workers[nextWorkerIndex];
  nextWorkerIndex = (nextWorkerIndex + 1) % workers.length;
  return worker;
}

/**
 * Returns the room with the given ID, creating it (and its mediasoup Router) if needed.
 * @param {string} roomId
 * @returns {Promise<Room>}
 */
async function getOrCreateRoom(roomId) {
  if (rooms.has(roomId)) return rooms.get(roomId);

  const worker = getNextWorker();
  const router = await worker.createRouter({ mediaCodecs: config.mediaCodecs });
  const room = new Room(roomId, router);
  rooms.set(roomId, room);

  // Start the per-room adaptive bitrate loop.
  room.abr = new RoomAbr(room, async (consumerId, spatial, temporal) => {
    const consumer = room.consumers.get(consumerId);
    if (!consumer) return;
    await consumer.setPreferredLayers({ spatialLayer: spatial, temporalLayer: temporal });
    consumer.requestKeyFrame().catch(() => {});
  });

  console.log(`Room created: ${roomId}`);
  return room;
}

// ─── Express HTTP API ─────────────────────────────────────────────────────────

const app = express();
app.use(express.json());

// Health check
app.get('/health', (_req, res) => res.json({ status: 'ok' }));

// Router RTP capabilities (same for all rooms because they share the same codecs)
app.get('/rtp-capabilities', (_req, res) => {
  if (!capabilitiesRouter) {
    return res.status(503).json({ error: 'SFU not ready yet' });
  }
  res.json(capabilitiesRouter.rtpCapabilities);
});

function requestKeyFrameForProducerConsumers(room, producerId, reason) {
  let matchedConsumers = 0;

  for (const consumer of room.consumers.values()) {
    if (consumer.closed || consumer.kind !== 'video' || consumer.producerId !== producerId) {
      continue;
    }

    matchedConsumers++;
    consumer.requestKeyFrame().catch((err) => {
      console.warn(`[server-stream] keyframe request failed producer=${producerId.slice(0, 8)} consumer=${consumer.id.slice(0, 8)} reason=${reason}: ${err.message}`);
    });
  }

  console.log(`[server-stream] keyframe requests producer=${producerId.slice(0, 8)} consumers=${matchedConsumers} reason=${reason}`);
}

// Create / ensure room
app.post('/rooms/:roomId', async (req, res) => {
  try {
    const room = await getOrCreateRoom(req.params.roomId);
    res.json({ roomId: room.id });
  } catch (err) {
    res.status(500).json({ error: String(err) });
  }
});

// Delete room
app.delete('/rooms/:roomId', (req, res) => {
  const room = rooms.get(req.params.roomId);
  if (!room) return res.status(404).json({ error: 'Room not found' });
  room.abr?.stop();
  if (room.serverFileStream) {
    stopServerFileStream(room, room.serverFileStream);
    room.serverFileStream = null;
  }
  room.router.close();
  rooms.delete(req.params.roomId);
  console.log(`Room closed: ${req.params.roomId}`);
  res.json({ ok: true });
});

// List active producers in a room
app.get('/rooms/:roomId/producers', (req, res) => {
  const room = rooms.get(req.params.roomId);
  if (!room) return res.status(404).json({ error: 'Room not found' });
  const list = [...room.producers.entries()].map(([id, { peerId }]) => ({
    id,
    kind: room.producers.get(id).producer.kind,
    peerId,
  }));
  res.json(list);
});

// Create WebRTC transport
app.post('/rooms/:roomId/transports', async (req, res) => {
  try {
    const room = await getOrCreateRoom(req.params.roomId);
    const transport = await room.router.createWebRtcTransport({
      listenInfos: [
        {
          protocol: 'udp',
          ip: '0.0.0.0',
          announcedAddress: config.announcedIp,
        },
        {
          protocol: 'tcp',
          ip: '0.0.0.0',
          announcedAddress: config.announcedIp,
        },
      ],
      enableUdp: true,
      enableTcp: true,
      preferUdp: true,
      initialAvailableOutgoingBitrate: config.initialAvailableOutgoingBitrate,
    });

    if (config.maxIncomingBitrate > 0) {
      await transport.setMaxIncomingBitrate(config.maxIncomingBitrate);
    }

    room.transports.set(transport.id, transport);

    transport.on('dtlsstatechange', (state) => {
      if (state === 'closed') room.transports.delete(transport.id);
    });

    res.json({
      id: transport.id,
      iceParameters: transport.iceParameters,
      iceCandidates: transport.iceCandidates,
      dtlsParameters: transport.dtlsParameters,
    });
  } catch (err) {
    res.status(500).json({ error: String(err) });
  }
});

// Connect transport (DTLS)
app.post('/rooms/:roomId/transports/:transportId/connect', async (req, res) => {
  try {
    const room = rooms.get(req.params.roomId);
    if (!room) return res.status(404).json({ error: 'Room not found' });
    const transport = room.transports.get(req.params.transportId);
    if (!transport) return res.status(404).json({ error: 'Transport not found' });
    await transport.connect({ dtlsParameters: req.body.dtlsParameters });
    res.json({ ok: true });
  } catch (err) {
    res.status(500).json({ error: String(err) });
  }
});

// Create producer on a send transport
app.post('/rooms/:roomId/transports/:transportId/produce', async (req, res) => {
  try {
    const room = rooms.get(req.params.roomId);
    if (!room) return res.status(404).json({ error: 'Room not found' });
    const transport = room.transports.get(req.params.transportId);
    if (!transport) return res.status(404).json({ error: 'Transport not found' });

    const { kind, rtpParameters, peerId = '', encodings } = req.body;
    const produceOptions = { kind, rtpParameters };
    // If the client sends simulcast encodings, forward them so mediasoup tracks them.
    if (Array.isArray(encodings) && encodings.length > 0) {
      produceOptions.encodings = encodings;
    }
    const producer = await transport.produce(produceOptions);

    room.producers.set(producer.id, { producer, peerId });

    producer.on('transportclose', () => room.producers.delete(producer.id));
    producer.on('score', (scores) => {
      const worst = Math.min(...scores.map(s => s.score));
      if (worst <= 4) {
        console.warn(`[score] producer ${producer.id.slice(0, 8)} score=${worst} (encoder bottleneck)`);
      }
    });

    res.json({ id: producer.id });
  } catch (err) {
    res.status(500).json({ error: String(err) });
  }
});

// Create consumer on a recv transport
app.post('/rooms/:roomId/transports/:transportId/consume', async (req, res) => {
  try {
    const room = rooms.get(req.params.roomId);
    if (!room) return res.status(404).json({ error: 'Room not found' });
    const transport = room.transports.get(req.params.transportId);
    if (!transport) return res.status(404).json({ error: 'Transport not found' });

    const { producerId, rtpCapabilities, peerId = '' } = req.body;
    const entry = room.producers.get(producerId);
    if (!entry) return res.status(404).json({ error: 'Producer not found' });

    if (!room.router.canConsume({ producerId, rtpCapabilities })) {
      return res.status(400).json({ error: 'Cannot consume: incompatible RTP capabilities' });
    }

    const consumer = await transport.consume({
      producerId,
      rtpCapabilities,
      paused: false,
    });

    room.consumers.set(consumer.id, consumer);
    consumer.on('transportclose', () => room.consumers.delete(consumer.id));
    consumer.on('producerclose', () => {
      room.consumers.delete(consumer.id);
      consumer.close();
    });
    consumer.on('score', (score) => {
      console.log(`[consumer] peer=${peerId || '-'} kind=${consumer.kind} producer=${consumer.producerId.slice(0, 8)} consumer=${consumer.id.slice(0, 8)} score=${JSON.stringify(score)}`);
    });

    // Request a keyframe immediately so the consumer renders without waiting for
    // Chrome's natural VP8 keyframe interval (which can be up to ~4 seconds).
    consumer.requestKeyFrame().catch(() => {});

    res.json({
      id: consumer.id,
      producerId: consumer.producerId,
      kind: consumer.kind,
      rtpParameters: consumer.rtpParameters,
    });
  } catch (err) {
    res.status(500).json({ error: String(err) });
  }
});

// Close a producer
app.delete('/rooms/:roomId/producers/:producerId', (req, res) => {
  const room = rooms.get(req.params.roomId);
  if (!room) return res.status(404).json({ error: 'Room not found' });
  const entry = room.producers.get(req.params.producerId);
  if (!entry) return res.status(404).json({ error: 'Producer not found' });
  entry.producer.close();
  room.producers.delete(req.params.producerId);
  res.json({ ok: true });
});

// Close a transport (and all its producers/consumers)
app.delete('/rooms/:roomId/transports/:transportId', (req, res) => {
  const room = rooms.get(req.params.roomId);
  if (!room) return res.status(404).json({ error: 'Room not found' });
  const transport = room.transports.get(req.params.transportId);
  if (!transport) return res.status(404).json({ error: 'Transport not found' });
  transport.close();
  room.transports.delete(req.params.transportId);
  res.json({ ok: true });
});

// Set preferred simulcast layers on a consumer (adaptive bitrate)
app.post('/rooms/:roomId/consumers/:consumerId/preferred-layers', async (req, res) => {
  try {
    const room = rooms.get(req.params.roomId);
    if (!room) return res.status(404).json({ error: 'Room not found' });
    const consumer = room.consumers.get(req.params.consumerId);
    if (!consumer) return res.status(404).json({ error: 'Consumer not found' });
    const { spatialLayer, temporalLayer } = req.body;
    await consumer.setPreferredLayers({ spatialLayer, temporalLayer });
    // Inform the ABR loop of the user's ceiling so it never auto-exceeds it.
    room.abr?.onUserSetLayers(req.params.consumerId, spatialLayer, temporalLayer);
    // Force a keyframe so the spatial-layer switch completes immediately.
    consumer.requestKeyFrame().catch(() => {});
    res.json({ ok: true });
  } catch (err) {
    res.status(500).json({ error: String(err) });
  }
});

// ─── Server-side file streaming ──────────────────────────────────────────────
//
// The .NET hub calls these endpoints to stream an arbitrary local file to all
// room participants via ffmpeg → PlainTransport → mediasoup producers.
// Consumers receive it identically to a regular peer producer.

// Start streaming a file from the server into the room.
// Body: { filePath: string, targetBitrate?: number }
// Response: { videoProducerId, audioProducerId }
app.post('/rooms/:roomId/server-stream', async (req, res) => {
  try {
    const room = await getOrCreateRoom(req.params.roomId);
    if (room.serverFileStream) {
      return res.status(409).json({ error: 'A server file stream is already active in this room' });
    }

    const { filePath, targetBitrate = 4_000_000 } = req.body;
    if (!filePath) return res.status(400).json({ error: 'filePath is required' });

    // Accumulate new producer IDs so we can return them in the response.
    const newProducers = [];

    room.serverFileStream = await startServerFileStream(
      room,
      filePath,
      targetBitrate,
      (producerId, kind, peerId) => {
        newProducers.push({ producerId, kind, peerId });
      }
    );

    res.json({
      videoProducerId: room.serverFileStream.producerIds.video,
      audioProducerId: room.serverFileStream.producerIds.audio,
    });
  } catch (err) {
    console.error('[server-stream] Failed to start:', err);
    res.status(500).json({ error: String(err) });
  }
});

// Play / pause / seek the active server file stream.
// Body: { action: 'play' | 'pause' | 'seek', position?: number }
app.post('/rooms/:roomId/server-stream/control', async (req, res) => {
  const room = rooms.get(req.params.roomId);
  if (!room) return res.status(404).json({ error: 'Room not found' });
  if (!room.serverFileStream) return res.status(404).json({ error: 'No active server stream' });

  const { action, position } = req.body;
  const stream = room.serverFileStream;

  try {
    if (action === 'pause') {
      await pauseServerFileStream(stream);
    } else if (action === 'play') {
      if (!stream.paused) return res.json({ ok: true, position: stream.currentPositionSec });
      await resumeServerFileStream(stream, null);
      requestKeyFrameForProducerConsumers(room, stream.videoProducer.id, 'play');
    } else if (action === 'seek') {
      const seekPos = typeof position === 'number' ? position : stream.positionSec;
      await resumeServerFileStream(stream, seekPos);
      requestKeyFrameForProducerConsumers(room, stream.videoProducer.id, 'seek');
    } else {
      return res.status(400).json({ error: `Unknown action: ${action}` });
    }
    res.json({ ok: true, position: stream.currentPositionSec });
  } catch (err) {
    console.error(`[server-stream] control action=${action} failed:`, err);
    res.status(500).json({ error: String(err) });
  }
});

// Stop the server file stream and clean up all resources.
app.delete('/rooms/:roomId/server-stream', (req, res) => {
  const room = rooms.get(req.params.roomId);
  if (!room) return res.status(404).json({ error: 'Room not found' });
  if (!room.serverFileStream) return res.status(404).json({ error: 'No active server stream' });

  stopServerFileStream(room, room.serverFileStream);
  room.serverFileStream = null;
  res.json({ ok: true });
});

// ─── Recording ────────────────────────────────────────────────────────────────
//
// Each producer's RTP is piped via mediasoup PlainTransport → FFmpeg stdin
// (using separate ffmpeg invocations per producer, muxed together at stop time).
// The final file is written to config.recordingsDir as a .mkv.

// Port allocator for FFmpeg RTP receivers.  Uses a range well outside the
// WebRTC media port range (10000-10099) to avoid collisions.
let _nextRecordingPort = 40000;
const _usedRecordingPorts = new Set();

function allocateRecordingPort() {
  const start = _nextRecordingPort;
  while (_usedRecordingPorts.has(_nextRecordingPort)) {
    _nextRecordingPort = (_nextRecordingPort >= 49999) ? 40000 : _nextRecordingPort + 1;
    if (_nextRecordingPort === start) throw new Error('No available recording port');
  }
  const port = _nextRecordingPort;
  _nextRecordingPort = (port >= 49999) ? 40000 : port + 1;
  _usedRecordingPorts.add(port);
  return port;
}

function freeRecordingPort(port) {
  _usedRecordingPorts.delete(port);
}

/**
 * Starts recording all active producers in a room.
 * Uses a single FFmpeg process that receives all producer streams over RTP
 * via separate PlainTransports and muxes them into one .mkv file.
 */
async function startRoomRecording(room) {
  if (room.recording) throw new Error('Recording already active');

  if (!fs.existsSync(config.recordingsDir)) {
    fs.mkdirSync(config.recordingsDir, { recursive: true });
  }

  const filename = `${room.id.replace(/[^a-z0-9_-]/gi, '_')}_${Date.now()}.mkv`;
  const outputPath = path.join(config.recordingsDir, filename);

  // Allocate one PlainTransport + consumer per producer.
  const inputs = []; // { sdp, rtpPort (FFmpeg's listening port), kind, consumerId }
  const plainTransports = [];

  for (const [producerId, { producer }] of room.producers) {
    if (!room.router.canConsume({ producerId, rtpCapabilities: room.router.rtpCapabilities })) continue;

    const plainTransport = await room.router.createPlainTransport({
      listenInfo: { protocol: 'udp', ip: '127.0.0.1' },
      rtcpMux: false,
      comedia: false,
    });
    plainTransports.push(plainTransport);

    // Allocate a port for FFmpeg to receive on — distinct from mediasoup's own port.
    const ffmpegRtpPort = allocateRecordingPort();
    // Tell mediasoup to forward RTP to FFmpeg's listening port.
    await plainTransport.connect({ ip: '127.0.0.1', port: ffmpegRtpPort });

    const consumer = await plainTransport.consume({
      producerId,
      rtpCapabilities: room.router.rtpCapabilities,
      paused: false,
    });
    room.consumers.set(consumer.id, consumer);

    const codec = consumer.rtpParameters.codecs[0];
    const sdp = buildRtpSdp(producer.kind, codec, ffmpegRtpPort);
    inputs.push({ sdp, rtpPort: ffmpegRtpPort, kind: producer.kind, consumerId: consumer.id });
  }

  if (inputs.length === 0) throw new Error('No consumable producers in room');

  // Build FFmpeg command: one -f sdp input per track, muxed to mkv.
  const ffmpegArgs = [];
  for (const input of inputs) {
    const sdpFile = path.join(config.recordingsDir, `sdp_${room.id}_${input.rtpPort}.sdp`);
    fs.writeFileSync(sdpFile, input.sdp);
    ffmpegArgs.push('-protocol_whitelist', 'file,rtp,udp', '-f', 'sdp', '-i', sdpFile);
  }

  // Map all inputs to the output.
  inputs.forEach((_, idx) => ffmpegArgs.push('-map', `${idx}:0`));
  ffmpegArgs.push('-c', 'copy', '-y', outputPath);

  const proc = spawn('ffmpeg', ffmpegArgs, { stdio: ['ignore', 'pipe', 'pipe'] });
  proc.stderr.on('data', d => process.stdout.write(`[FFmpeg ${room.id}] ${d}`));

  const startedAtUnixMs = Date.now();
  room.recording = { process: proc, filename, startedAtUnixMs, plainTransports, inputs };
  console.log(`Recording started for room ${room.id}: ${outputPath}`);
  return { filename, startedAtUnixMs };
}

/**
 * Stops the active recording in a room and cleans up resources.
 */
async function stopRoomRecording(room) {
  if (!room.recording) throw new Error('No active recording');

  const { process: proc, filename, plainTransports, inputs } = room.recording;
  room.recording = null;

  // Gracefully stop FFmpeg.
  proc.stdin?.end();
  proc.kill('SIGTERM');
  await new Promise(resolve => proc.on('close', resolve));

  // Clean up plain transports, temporary SDP files, and release allocated ports.
  for (const pt of plainTransports) { try { pt.close(); } catch { /* ignore */ } }
  for (const input of inputs) {
    freeRecordingPort(input.rtpPort);
    const sdpFile = path.join(config.recordingsDir, `sdp_${room.id}_${input.rtpPort}.sdp`);
    try { fs.unlinkSync(sdpFile); } catch { /* ignore */ }
  }

  console.log(`Recording stopped for room ${room.id}: ${filename}`);
  return filename;
}

/**
 * Builds a minimal SDP for a single RTP stream so FFmpeg can decode it.
 */
function buildRtpSdp(kind, codec, rtpPort) {
  const mediaType = kind === 'audio' ? 'audio' : 'video';
  const pt = codec.payloadType;
  const encoding = codec.mimeType.split('/')[1].toUpperCase();
  const clockRate = codec.clockRate;
  const channels = codec.channels ?? (kind === 'audio' ? 2 : undefined);
  const fmtp = codec.parameters ? Object.entries(codec.parameters).map(([k, v]) => `${k}=${v}`).join(';') : null;

  let sdp = `v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=mediasoup recording\r\nc=IN IP4 127.0.0.1\r\nt=0 0\r\n`;
  sdp += `m=${mediaType} ${rtpPort} RTP/AVP ${pt}\r\n`;
  sdp += `a=rtpmap:${pt} ${encoding}/${clockRate}${channels ? `/${channels}` : ''}\r\n`;
  if (fmtp) sdp += `a=fmtp:${pt} ${fmtp}\r\n`;
  sdp += `a=recvonly\r\n`;
  return sdp;
}

app.post('/rooms/:roomId/recording/start', async (req, res) => {
  try {
    const room = rooms.get(req.params.roomId);
    if (!room) return res.status(404).json({ error: 'Room not found' });
    const result = await startRoomRecording(room);
    res.json(result);
  } catch (err) {
    res.status(500).json({ error: String(err) });
  }
});

app.post('/rooms/:roomId/recording/stop', async (req, res) => {
  try {
    const room = rooms.get(req.params.roomId);
    if (!room) return res.status(404).json({ error: 'Room not found' });
    const filename = await stopRoomRecording(room);
    res.json({ ok: true, filename });
  } catch (err) {
    res.status(500).json({ error: String(err) });
  }
});

// ─── Main ─────────────────────────────────────────────────────────────────────


(async () => {
  await startWorkers();

  app.listen(config.httpPort, '0.0.0.0', () => {
    console.log(`SFU HTTP API listening on port ${config.httpPort}`);
    console.log(`Announced IP: ${config.announcedIp}`);
    console.log(`RTC port range: ${config.rtcMinPort}-${config.rtcMaxPort}`);
  });
})().catch((err) => {
  console.error('Fatal error during startup:', err);
  process.exit(1);
});
