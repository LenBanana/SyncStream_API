'use strict';

const express = require('express');
const mediasoup = require('mediasoup');
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const os = require('os');
const {
  StreamError,
  startServerFileStream,
  pauseServerFileStream,
  playServerFileStream,
  markUploadComplete,
  stopServerFileStream,
} = require('./server-file-streamer');

// ─── Configuration ────────────────────────────────────────────────────────────

const numWorkers = process.env.SFU_NUM_WORKERS
  ? parseInt(process.env.SFU_NUM_WORKERS, 10)
  : os.cpus().length;
const rtcMinPort = parseInt(process.env.SFU_RTC_MIN_PORT ?? '10000', 10);
const rtcMaxPort = parseInt(process.env.SFU_RTC_MAX_PORT ?? '10099', 10);

const config = {
  httpPort: parseInt(process.env.SFU_HTTP_PORT ?? '3000', 10),

  // The control API is unauthenticated, so it listens on loopback unless told otherwise. Under Docker
  // bridge networking the API reaches this container by name, which needs 0.0.0.0 (the port is not
  // published, so that is still private to the Docker network).
  httpBindAddress: process.env.SFU_HTTP_BIND ?? '127.0.0.1',

  // Public IP of the server (announced in ICE candidates so browsers can reach us).
  announcedIp: process.env.SFU_ANNOUNCED_IP ?? '152.53.224.14',

  numWorkers,

  // Every browser transport of a worker shares that worker's WebRtcServer, on one UDP and one TCP port:
  // the first ports of the configured range, one per worker. A port per transport capped the server at
  // as many transports as the range had ports.
  webRtcServerPort: (workerIndex) => rtcMinPort + workerIndex,

  // The rest of the range is left to the workers for the loopback PlainTransports used by file
  // streaming and recording.
  workerMinPort: rtcMinPort + numWorkers,
  workerMaxPort: rtcMaxPort,

  // Transport bitrate tuning.  This deployment prioritizes quality over density,
  // so we start transports with a much higher outgoing budget than mediasoup's
  // conservative defaults and optionally allow higher ingress from producers.
  initialAvailableOutgoingBitrate: parseInt(process.env.SFU_INITIAL_OUTGOING_BITRATE ?? '25000000', 10),
  maxIncomingBitrate: parseInt(process.env.SFU_MAX_INCOMING_BITRATE ?? '50000000', 10),

  // Throttles keyframe requests to a producer. Without it, every viewer that loses a frame asks the
  // streamer's encoder for a keyframe, and one viewer on a bad link inflates the bitrate for everyone.
  keyFrameRequestDelayMs: parseInt(process.env.SFU_KEYFRAME_REQUEST_DELAY_MS ?? '1000', 10),

  // A room with no transports, no server stream and no recording for this long is closed.
  roomIdleMs: parseInt(process.env.SFU_ROOM_IDLE_MS ?? '60000', 10),

  // Directory where recordings are written.  Mount this as a shared volume
  // so the .NET API can serve finished files.
  recordingsDir: process.env.SFU_RECORDINGS_DIR ?? '/recordings',

  // Media codecs supported by the router.
  //
  // rtcpFeedback is REQUIRED on every video codec: without it, mediasoup never
  // synthesises an RTX (retransmission) entry for browser consumers, and a
  // single packet lost on the SFU→browser leg becomes a visible decode stutter
  // that no encoder tuning can recover.  With the feedback list below:
  //   * `nack` enables RTX: mediasoup buffers ~1s of producer packets and
  //     retransmits on browser NACK from a paired RTX SSRC.
  //   * `nack pli` and `ccm fir` let consumers request a fresh keyframe when
  //     their decoder gets stuck (mediasoup forwards these to live producers;
  //     for our PlainTransport file source it can't act on them, but the path
  //     must be open for browser-source streams in the same room).
  //   * `transport-cc` and `goog-remb` give the browser bandwidth-estimation
  //     feedback so it sizes its jitter buffer correctly.
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
      rtcpFeedback: [
        { type: 'nack' },
        { type: 'nack', parameter: 'pli' },
        { type: 'ccm', parameter: 'fir' },
        { type: 'goog-remb' },
        { type: 'transport-cc' },
      ],
    },
    {
      kind: 'video',
      mimeType: 'video/VP9',
      clockRate: 90000,
      parameters: {
        'x-google-start-bitrate': 1000,
      },
      rtcpFeedback: [
        { type: 'nack' },
        { type: 'nack', parameter: 'pli' },
        { type: 'ccm', parameter: 'fir' },
        { type: 'goog-remb' },
        { type: 'transport-cc' },
      ],
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
      rtcpFeedback: [
        { type: 'nack' },
        { type: 'nack', parameter: 'pli' },
        { type: 'ccm', parameter: 'fir' },
        { type: 'goog-remb' },
        { type: 'transport-cc' },
      ],
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
      rtcpFeedback: [
        { type: 'nack' },
        { type: 'nack', parameter: 'pli' },
        { type: 'ccm', parameter: 'fir' },
        { type: 'goog-remb' },
        { type: 'transport-cc' },
      ],
    },
  ],
};

if (config.workerMinPort > config.workerMaxPort) {
  throw new Error(`SFU_RTC_MIN_PORT..SFU_RTC_MAX_PORT (${rtcMinPort}-${rtcMaxPort}) needs more than ${numWorkers} ports: ` +
    'one WebRtcServer port per worker plus at least one for PlainTransports.');
}

// ─── State ────────────────────────────────────────────────────────────────────

/** @type {{ worker: mediasoup.types.Worker, webRtcServer: mediasoup.types.WebRtcServer }[]} */
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
   * @param {mediasoup.types.WebRtcServer} webRtcServer
   */
  constructor(id, router, webRtcServer) {
    this.id = id;
    this.router = router;
    this.webRtcServer = webRtcServer;
    /** @type {Map<string, mediasoup.types.WebRtcTransport>} transportId → transport */
    this.transports = new Map();
    /** @type {Map<string, { producer: mediasoup.types.Producer, peerId: string }>} producerId → { producer, peerId } */
    this.producers = new Map();
    /** @type {Map<string, mediasoup.types.Consumer>} consumerId → consumer */
    this.consumers = new Map();
    /** @type {{ process: import('child_process').ChildProcess, filename: string, startedAtUnixMs: number, plainTransports: mediasoup.types.PlainTransport[], inputs: object[] } | null} */
    this.recording = null;
    /** @type {import('./server-file-streamer').ServerFileStream | null} */
    this.serverFileStream = null;
    /** When the room last became unused; null while it is in use. */
    this.idleSince = Date.now();
  }

  get inUse() {
    return this.transports.size > 0 || this.serverFileStream !== null || this.recording !== null;
  }
}

// ─── Startup ─────────────────────────────────────────────────────────────────

async function startWorkers() {
  for (let i = 0; i < config.numWorkers; i++) {
    const worker = await mediasoup.createWorker({
      rtcMinPort: config.workerMinPort,
      rtcMaxPort: config.workerMaxPort,
      logLevel: 'warn',
      logTags: ['rtp', 'srtp', 'rtcp', 'rtx', 'bwe', 'score', 'simulcast', 'svc'],
    });

    worker.on('died', (err) => {
      console.error(`mediasoup Worker ${i} died:`, err);
      process.exit(1);
    });

    const port = config.webRtcServerPort(i);
    const webRtcServer = await worker.createWebRtcServer({
      listenInfos: [
        { protocol: 'udp', ip: '0.0.0.0', announcedAddress: config.announcedIp, port },
        { protocol: 'tcp', ip: '0.0.0.0', announcedAddress: config.announcedIp, port },
      ],
    });

    workers.push({ worker, webRtcServer });
    console.log(`mediasoup Worker ${i} pid=${worker.pid} webRtcServer=${port}/udp+tcp`);
  }

  // Create the shared capabilities router on the first worker.
  capabilitiesRouter = await workers[0].worker.createRouter({ mediaCodecs: config.mediaCodecs });
  console.log('Capabilities router ready');
}

/** Round-robin worker selection. */
function getNextWorker() {
  const entry = workers[nextWorkerIndex];
  nextWorkerIndex = (nextWorkerIndex + 1) % workers.length;
  return entry;
}

/** Rooms whose router is being created, so concurrent requests share one creation. */
const pendingRooms = new Map();

/**
 * Returns the room with the given ID, creating it (and its mediasoup Router) if needed.
 * @param {string} roomId
 * @returns {Promise<Room>}
 */
async function getOrCreateRoom(roomId) {
  const existing = rooms.get(roomId);
  if (existing) return existing;
  // Two requests for a new room must not each create a router for it.
  if (pendingRooms.has(roomId)) return pendingRooms.get(roomId);

  const creation = (async () => {
    const { worker, webRtcServer } = getNextWorker();
    const router = await worker.createRouter({ mediaCodecs: config.mediaCodecs });
    const room = new Room(roomId, router, webRtcServer);
    rooms.set(roomId, room);
    console.log(`Room created: ${roomId}`);
    return room;
  })();
  pendingRooms.set(roomId, creation);
  try {
    return await creation;
  } finally {
    pendingRooms.delete(roomId);
  }
}

function closeRoom(room) {
  if (room.serverFileStream) {
    stopServerFileStream(room.serverFileStream);
    room.serverFileStream = null;
  }
  room.router.close();
  rooms.delete(room.id);
  console.log(`Room closed: ${room.id}`);
}

/** Closes rooms nothing has used for config.roomIdleMs. */
function sweepIdleRooms() {
  const now = Date.now();
  for (const room of rooms.values()) {
    if (room.inUse) {
      room.idleSince = null;
      continue;
    }
    if (room.idleSince === null) {
      room.idleSince = now;
      continue;
    }
    if (now - room.idleSince >= config.roomIdleMs) closeRoom(room);
  }
}

function registerProducer(room, producer, peerId) {
  room.producers.set(producer.id, { producer, peerId });
  producer.observer.on('close', () => room.producers.delete(producer.id));
}

// ─── Express HTTP API ─────────────────────────────────────────────────────────

class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

/** Wraps a handler so a thrown error becomes a JSON error response with the right status. */
const route = (handler) => async (req, res) => {
  try {
    await handler(req, res);
  } catch (err) {
    const status = err instanceof HttpError || err instanceof StreamError ? err.status : 500;
    if (status >= 500) console.error(`[http] ${req.method} ${req.path} failed:`, err);
    res.status(status).json({ error: err.message ?? String(err) });
  }
};

function requireRoom(roomId) {
  const room = rooms.get(roomId);
  if (!room) throw new HttpError(404, 'Room not found');
  return room;
}

function requireTransport(room, transportId) {
  const transport = room.transports.get(transportId);
  if (!transport) throw new HttpError(404, 'Transport not found');
  return transport;
}

function requireServerStream(room) {
  if (!room.serverFileStream) throw new HttpError(404, 'No active server stream');
  return room.serverFileStream;
}

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

const app = express();
app.use(express.json({ limit: '1mb' }));

// Health check
app.get('/health', (_req, res) => res.json({ status: 'ok' }));

// Router RTP capabilities (same for all rooms because they share the same codecs)
app.get('/rtp-capabilities', (_req, res) => {
  if (!capabilitiesRouter) {
    return res.status(503).json({ error: 'SFU not ready yet' });
  }
  res.json(capabilitiesRouter.rtpCapabilities);
});

// Create / ensure room
app.post('/rooms/:roomId', route(async (req, res) => {
  const room = await getOrCreateRoom(req.params.roomId);
  res.json({ roomId: room.id });
}));

// List active producers in a room. A room that does not exist yet simply has none.
app.get('/rooms/:roomId/producers', (req, res) => {
  const room = rooms.get(req.params.roomId);
  if (!room) return res.json([]);
  const list = [...room.producers.entries()].map(([id, { producer, peerId }]) => ({
    id,
    kind: producer.kind,
    peerId,
  }));
  res.json(list);
});

// Create WebRTC transport
app.post('/rooms/:roomId/transports', route(async (req, res) => {
  const room = await getOrCreateRoom(req.params.roomId);
  const transport = await room.router.createWebRtcTransport({
    webRtcServer: room.webRtcServer,
    enableUdp: true,
    enableTcp: true,
    preferUdp: true,
    initialAvailableOutgoingBitrate: config.initialAvailableOutgoingBitrate,
  });

  if (config.maxIncomingBitrate > 0) {
    await transport.setMaxIncomingBitrate(config.maxIncomingBitrate);
  }

  room.transports.set(transport.id, transport);
  room.idleSince = null;
  transport.observer.on('close', () => room.transports.delete(transport.id));

  res.json({
    id: transport.id,
    iceParameters: transport.iceParameters,
    iceCandidates: transport.iceCandidates,
    dtlsParameters: transport.dtlsParameters,
  });
}));

// Connect transport (DTLS)
app.post('/rooms/:roomId/transports/:transportId/connect', route(async (req, res) => {
  const transport = requireTransport(requireRoom(req.params.roomId), req.params.transportId);
  await transport.connect({ dtlsParameters: req.body.dtlsParameters });
  res.json({ ok: true });
}));

// New ICE credentials for a transport whose connection failed, so the browser can re-establish it.
app.post('/rooms/:roomId/transports/:transportId/restart-ice', route(async (req, res) => {
  const transport = requireTransport(requireRoom(req.params.roomId), req.params.transportId);
  const iceParameters = await transport.restartIce();
  res.json({ iceParameters });
}));

// Create producer on a send transport
app.post('/rooms/:roomId/transports/:transportId/produce', route(async (req, res) => {
  const room = requireRoom(req.params.roomId);
  const transport = requireTransport(room, req.params.transportId);

  const { kind, rtpParameters, peerId = '' } = req.body;
  if (kind !== 'audio' && kind !== 'video') throw new HttpError(400, 'kind must be audio or video');

  const producer = await transport.produce({
    kind,
    rtpParameters,
    keyFrameRequestDelay: kind === 'video' ? config.keyFrameRequestDelayMs : undefined,
  });
  registerProducer(room, producer, peerId);

  producer.on('score', (scores) => {
    const worst = Math.min(...scores.map(s => s.score));
    if (worst <= 4) {
      console.warn(`[score] producer ${producer.id.slice(0, 8)} score=${worst} (sender side losing packets)`);
    }
  });

  res.json({ id: producer.id });
}));

// Create consumer on a recv transport
app.post('/rooms/:roomId/transports/:transportId/consume', route(async (req, res) => {
  const room = requireRoom(req.params.roomId);
  const transport = requireTransport(room, req.params.transportId);

  const { producerId, rtpCapabilities, peerId = '' } = req.body;
  const entry = room.producers.get(producerId);
  if (!entry) throw new HttpError(404, 'Producer not found');

  if (!room.router.canConsume({ producerId, rtpCapabilities })) {
    // Most common cause: the producer's codec (e.g. H.264 Main 4d0032) has no
    // compatible match in the consumer's device caps.  Log enough detail to
    // diagnose without re-deploying.
    const describeCodec = (c) => `${c.mimeType}${c.parameters?.['profile-level-id'] ? ` (${c.parameters['profile-level-id']})` : ''}`;
    const producerCodecs = entry.producer.rtpParameters?.codecs?.map(describeCodec) ?? [];
    const consumerVideoCodecs = (rtpCapabilities?.codecs ?? []).filter((c) => c.kind === 'video').map(describeCodec);
    console.warn(
      `[consume] canConsume=false producer=${producerId.slice(0, 8)} ` +
      `producerCodecs=[${producerCodecs.join(', ')}] ` +
      `consumerVideoCodecs=[${consumerVideoCodecs.join(', ')}]`
    );
    throw new HttpError(400, 'Cannot consume: incompatible RTP capabilities');
  }

  const consumer = await transport.consume({
    producerId,
    rtpCapabilities,
    paused: false,
  });

  room.consumers.set(consumer.id, consumer);
  consumer.observer.on('close', () => room.consumers.delete(consumer.id));
  consumer.on('producerclose', () => consumer.close());
  consumer.on('score', (score) => {
    if (score.score <= 4) {
      console.warn(`[consumer] peer=${peerId || '-'} kind=${consumer.kind} consumer=${consumer.id.slice(0, 8)} score=${score.score} (viewer link losing packets)`);
    }
  });

  // Request a keyframe immediately so the consumer renders without waiting
  // for the next natural keyframe (every ~2.5 s for server file streams,
  // up to several seconds for browser-source producers).
  consumer.requestKeyFrame().catch((err) => {
    console.warn(`[consume] initial keyframe request failed consumer=${consumer.id.slice(0, 8)}: ${err.message}`);
  });

  res.json({
    id: consumer.id,
    producerId: consumer.producerId,
    kind: consumer.kind,
    rtpParameters: consumer.rtpParameters,
  });
}));

// Close a producer
app.delete('/rooms/:roomId/producers/:producerId', route(async (req, res) => {
  const room = requireRoom(req.params.roomId);
  const entry = room.producers.get(req.params.producerId);
  if (!entry) throw new HttpError(404, 'Producer not found');
  entry.producer.close();
  res.json({ ok: true });
}));

// Close a transport (and all its producers/consumers)
app.delete('/rooms/:roomId/transports/:transportId', route(async (req, res) => {
  const transport = requireTransport(requireRoom(req.params.roomId), req.params.transportId);
  transport.close();
  res.json({ ok: true });
}));

// ─── Server-side file streaming ──────────────────────────────────────────────
//
// The .NET hub calls these endpoints to stream a file on disk to all room participants via
// ffmpeg → PlainTransport → mediasoup producers. Consumers receive it identically to a regular
// peer producer.

// Body: { filePath: string, targetBitrate?: number, growing?: boolean }
app.post('/rooms/:roomId/server-stream', route(async (req, res) => {
  const room = await getOrCreateRoom(req.params.roomId);
  if (room.serverFileStream) {
    throw new HttpError(409, 'A server file stream is already active in this room');
  }

  const { filePath, targetBitrate = 3_000_000, growing = false } = req.body;
  if (typeof filePath !== 'string' || !filePath) throw new HttpError(400, 'filePath is required');

  const stream = await startServerFileStream(room, {
    filePath,
    targetBitrate: Number(targetBitrate) > 0 ? Number(targetBitrate) : 3_000_000,
    growing: growing === true,
  });
  room.serverFileStream = stream;
  room.idleSince = null;
  for (const producer of stream.producers) registerProducer(room, producer, `server-file:${room.id}`);

  res.json(stream.status());
}));

app.get('/rooms/:roomId/server-stream', route(async (req, res) => {
  res.json(requireServerStream(requireRoom(req.params.roomId)).status());
}));

// Body: { action: 'play' | 'pause' | 'seek' | 'upload-complete', position?: number }
app.post('/rooms/:roomId/server-stream/control', route(async (req, res) => {
  const room = requireRoom(req.params.roomId);
  const stream = requireServerStream(room);
  const { action, position } = req.body;

  switch (action) {
    case 'pause':
      await pauseServerFileStream(stream);
      break;
    case 'play':
      await playServerFileStream(stream, null);
      requestKeyFrameForProducerConsumers(room, stream.video.producer.id, 'play');
      break;
    case 'seek':
      if (typeof position !== 'number' || !Number.isFinite(position)) throw new HttpError(400, 'position is required');
      await playServerFileStream(stream, position);
      requestKeyFrameForProducerConsumers(room, stream.video.producer.id, 'seek');
      break;
    case 'upload-complete':
      markUploadComplete(stream);
      break;
    default:
      throw new HttpError(400, `Unknown action: ${action}`);
  }

  res.json({ ok: true, ...stream.status() });
}));

// Stop the server file stream and clean up all resources.
app.delete('/rooms/:roomId/server-stream', route(async (req, res) => {
  const room = requireRoom(req.params.roomId);
  stopServerFileStream(requireServerStream(room));
  room.serverFileStream = null;
  res.json({ ok: true });
}));

// ─── Recording ────────────────────────────────────────────────────────────────
//
// Each producer's RTP is forwarded by a mediasoup PlainTransport to its own local
// UDP port, and one FFmpeg process reads all of them (one SDP input each) and
// copies them into a single .mkv in config.recordingsDir.

// Port allocator for FFmpeg RTP receivers.  Uses a range well outside the
// WebRTC media port range to avoid collisions.  Even ports only: an SDP input
// makes ffmpeg listen for RTP on the port and for RTCP on the port above it.
let _nextRecordingPort = 40000;
const _usedRecordingPorts = new Set();

function allocateRecordingPort() {
  const start = _nextRecordingPort;
  while (_usedRecordingPorts.has(_nextRecordingPort)) {
    _nextRecordingPort = (_nextRecordingPort >= 49998) ? 40000 : _nextRecordingPort + 2;
    if (_nextRecordingPort === start) throw new Error('No available recording port');
  }
  const port = _nextRecordingPort;
  _nextRecordingPort = (port >= 49998) ? 40000 : port + 2;
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
  if (room.recording) throw new HttpError(409, 'Recording already active');

  if (!fs.existsSync(config.recordingsDir)) {
    fs.mkdirSync(config.recordingsDir, { recursive: true });
  }

  const filename = `${room.id.replace(/[^a-z0-9_-]/gi, '_')}_${Date.now()}.mkv`;
  const outputPath = path.join(config.recordingsDir, filename);

  // Allocate one PlainTransport + consumer per producer.
  const inputs = []; // { sdp, rtpPort (FFmpeg's listening port), kind, consumerId }
  const plainTransports = [];
  const videoConsumers = [];

  for (const [producerId, { producer }] of room.producers) {
    if (!room.router.canConsume({ producerId, rtpCapabilities: room.router.rtpCapabilities })) continue;

    const plainTransport = await room.router.createPlainTransport({
      listenInfo: { protocol: 'udp', ip: '127.0.0.1' },
      rtcpMux: false,
      comedia: false,
    });
    plainTransports.push(plainTransport);

    // Allocate a port for FFmpeg to receive on, distinct from mediasoup's own port.
    const ffmpegRtpPort = allocateRecordingPort();
    // Tell mediasoup to forward RTP to FFmpeg's listening port, and RTCP to the one above it.
    await plainTransport.connect({ ip: '127.0.0.1', port: ffmpegRtpPort, rtcpPort: ffmpegRtpPort + 1 });

    const consumer = await plainTransport.consume({
      producerId,
      rtpCapabilities: room.router.rtpCapabilities,
      paused: false,
    });
    room.consumers.set(consumer.id, consumer);
    consumer.observer.on('close', () => room.consumers.delete(consumer.id));
    if (consumer.kind === 'video') videoConsumers.push(consumer);

    const codec = consumer.rtpParameters.codecs[0];
    const sdp = buildRtpSdp(producer.kind, codec, ffmpegRtpPort);
    inputs.push({ sdp, rtpPort: ffmpegRtpPort, kind: producer.kind, consumerId: consumer.id });
  }

  if (inputs.length === 0) {
    plainTransports.forEach(pt => pt.close());
    throw new HttpError(409, 'No consumable producers in room');
  }

  // ffmpeg opens its inputs one after another and probes each before opening the next, and a port is only
  // listened on once its input is opened: whatever arrives earlier is lost. Audio goes first because one
  // packet is enough to probe it; video can only be probed from a keyframe, so its probe is capped and
  // keyframes are asked for over the first seconds (see below).
  inputs.sort((a, b) => (a.kind === 'audio' ? 0 : 1) - (b.kind === 'audio' ? 0 : 1));
  const ffmpegArgs = [];
  for (const input of inputs) {
    const sdpFile = path.join(config.recordingsDir, `sdp_${room.id}_${input.rtpPort}.sdp`);
    fs.writeFileSync(sdpFile, input.sdp);
    ffmpegArgs.push('-protocol_whitelist', 'file,rtp,udp', '-analyzeduration', '3000000', '-f', 'sdp', '-i', sdpFile);
  }

  // Map all inputs to the output.
  inputs.forEach((_, idx) => ffmpegArgs.push('-map', `${idx}:0`));
  ffmpegArgs.push('-c', 'copy', '-y', outputPath);

  const proc = spawn('ffmpeg', ffmpegArgs, { stdio: ['ignore', 'pipe', 'pipe'] });
  proc.stderr.on('data', d => process.stdout.write(`[FFmpeg ${room.id}] ${d}`));

  // A recording that starts between keyframes has nothing decodable until the next one arrives, and the
  // video input may not be listening yet when the first one does. The producer throttles these to one a
  // second, so asking a few times costs nothing.
  for (const delay of [300, 1300, 2300]) {
    setTimeout(() => {
      for (const consumer of videoConsumers) {
        if (consumer.closed) continue;
        consumer.requestKeyFrame().catch((err) => console.warn(`[recording] keyframe request failed: ${err.message}`));
      }
    }, delay);
  }

  const startedAtUnixMs = Date.now();
  room.recording = { process: proc, filename, startedAtUnixMs, plainTransports, inputs };
  room.idleSince = null;
  console.log(`Recording started for room ${room.id}: ${outputPath}`);
  return { filename, startedAtUnixMs };
}

/**
 * Stops the active recording in a room and cleans up resources.
 */
async function stopRoomRecording(room) {
  if (!room.recording) throw new HttpError(409, 'No active recording');

  const { process: proc, filename, plainTransports, inputs } = room.recording;
  room.recording = null;

  // Gracefully stop FFmpeg so it finalises the container.
  const closed = new Promise(resolve => proc.on('close', resolve));
  proc.kill('SIGINT');
  await closed;

  // Clean up plain transports, temporary SDP files, and release allocated ports.
  for (const pt of plainTransports) pt.close();
  for (const input of inputs) {
    freeRecordingPort(input.rtpPort);
    const sdpFile = path.join(config.recordingsDir, `sdp_${room.id}_${input.rtpPort}.sdp`);
    fs.rmSync(sdpFile, { force: true });
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

app.post('/rooms/:roomId/recording/start', route(async (req, res) => {
  res.json(await startRoomRecording(requireRoom(req.params.roomId)));
}));

app.post('/rooms/:roomId/recording/stop', route(async (req, res) => {
  const filename = await stopRoomRecording(requireRoom(req.params.roomId));
  res.json({ ok: true, filename });
}));

// ─── Main ─────────────────────────────────────────────────────────────────────

(async () => {
  await startWorkers();
  setInterval(sweepIdleRooms, Math.max(1000, Math.min(15_000, config.roomIdleMs / 2)));

  app.listen(config.httpPort, config.httpBindAddress, () => {
    console.log(`SFU HTTP API listening on ${config.httpBindAddress}:${config.httpPort}`);
    console.log(`Announced IP: ${config.announcedIp}`);
    console.log(`WebRtcServer ports: ${config.webRtcServerPort(0)}-${config.webRtcServerPort(config.numWorkers - 1)}, ` +
      `PlainTransport range: ${config.workerMinPort}-${config.workerMaxPort}`);
  });
})().catch((err) => {
  console.error('Fatal error during startup:', err);
  process.exit(1);
});
