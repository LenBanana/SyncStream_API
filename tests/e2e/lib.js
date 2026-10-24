'use strict';
// Shared plumbing for the end-to-end scenarios.
const {execFileSync, spawn} = require('child_process');
const http = require('http');
const fs = require('fs');
const path = require('path');
const puppeteer = require('puppeteer-core');
const signalR = require('@microsoft/signalr');

const APP = 'http://localhost:4300';
const API = 'http://127.0.0.1:5000';
const ROOM = 'e2e';
const sleep = ms => new Promise(r => setTimeout(r, ms));

// ─── Results ──────────────────────────────────────────────────────────────────

const results = [];
function check(label, ok, detail) {
  results.push({label, ok});
  const suffix = detail === undefined ? '' : ` :: ${typeof detail === 'string' ? detail : JSON.stringify(detail)}`;
  console.log(`${ok ? 'PASS' : 'FAIL'} ${label}${suffix}`);
  return ok;
}

async function scenario(name, body) {
  console.log(`\n=== ${name}`);
  try {
    await body();
  } catch (err) {
    check(`${name} ran without throwing`, false, err && err.stack ? err.stack.split('\n').slice(0, 4).join(' | ') : String(err));
  }
}

// ─── Accounts and hub clients ────────────────────────────────────────────────

async function register(username, password, privileges) {
  const res = await fetch(`${API}/api/auth/register`, {
    method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({username, password})
  });
  if (res.status !== 200 && res.status !== 409) throw new Error(`register ${username}: ${res.status} ${await res.text()}`);
  execFileSync('docker', ['exec', 'dtest-db-1', 'psql', '-U', 'postgres', '-d', 'syncstream', '-c',
    `UPDATE "Users" SET approved = 1, userprivileges = ${privileges} WHERE username = '${username}'`]);
}

async function login(username, password) {
  const res = await fetch(`${API}/api/auth/login`, {
    method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({username, password})
  });
  if (res.status !== 200) throw new Error(`login ${username}: ${res.status} ${await res.text()}`);
  const cookie = res.headers.get('set-cookie').split(';')[0];
  return cookie;
}

async function hubClient(cookie) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API}/server`, {headers: cookie ? {Cookie: cookie} : {}})
    .configureLogging(signalR.LogLevel.None)
    .build();
  await connection.start();
  return connection;
}

/** The message a refused hub call carries, or null when it succeeded. */
async function refusal(promise) {
  try {
    await promise;
    return null;
  } catch (err) {
    const match = String(err.message).match(/HubException: (.*)$/);
    return match ? match[1] : String(err.message);
  }
}

// ─── Fixtures over HTTP ───────────────────────────────────────────────────────

let fixtureServer = null;
function serveFixtures(dir) {
  fixtureServer = http.createServer((req, res) => {
    const file = path.join(dir, path.basename(req.url));
    res.setHeader('Access-Control-Allow-Origin', '*');
    if (!fs.existsSync(file)) { res.statusCode = 404; return res.end(); }
    fs.createReadStream(file).pipe(res);
  }).listen(4400);
}

// ─── Browsers ────────────────────────────────────────────────────────────────

let browser = null;

async function launch() {
  browser = await puppeteer.launch({
    executablePath: process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
    headless: true,
    protocolTimeout: 180000,
    args: [
      '--use-fake-ui-for-media-stream',
      '--use-fake-device-for-media-stream',
      '--auto-select-desktop-capture-source=Entire screen',
      '--autoplay-policy=no-user-gesture-required',
      '--disable-background-timer-throttling',
      '--disable-renderer-backgrounding',
      '--disable-backgrounding-occluded-windows',
    ],
  });
}

/** Helpers available inside every page as window.__e2e. */
function pageHelpers() {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  window.__e2e = {
    sleep,
    comp(selector) {
      const el = document.querySelector(selector);
      return el ? window.ng.getComponent(el) : null;
    },
    detect(selector) {
      const c = window.__e2e.comp(selector);
      if (c) window.ng.applyChanges(c);
    },
    /** Decoded frames of a stream's video over a period, through a video element of its own. */
    async frames(stream, ms = 2000) {
      if (!stream || stream.getVideoTracks().length === 0) return {frames: 0, width: 0, height: 0};
      const video = document.createElement('video');
      video.muted = true;
      video.playsInline = true;
      video.srcObject = new MediaStream(stream.getVideoTracks());
      document.body.appendChild(video);
      let frames = 0;
      const tick = () => { frames++; video.requestVideoFrameCallback(tick); };
      video.requestVideoFrameCallback(tick);
      await video.play().catch(() => undefined);
      await sleep(ms);
      const result = {frames, width: video.videoWidth, height: video.videoHeight};
      video.srcObject = null;
      video.remove();
      return result;
    },
    /** Peak RMS of an audio track over a period. */
    async rms(track, ms = 1200) {
      if (!track) return -1;
      const holder = new Audio();
      holder.muted = true;
      holder.srcObject = new MediaStream([track]);
      await holder.play().catch(() => undefined);
      const ctx = new AudioContext();
      const analyser = ctx.createAnalyser();
      ctx.createMediaStreamSource(new MediaStream([track])).connect(analyser);
      const buffer = new Float32Array(analyser.fftSize);
      let peak = 0;
      const end = Date.now() + ms;
      while (Date.now() < end) {
        await sleep(60);
        analyser.getFloatTimeDomainData(buffer);
        peak = Math.max(peak, Math.sqrt(buffer.reduce((a, v) => a + v * v, 0) / buffer.length));
      }
      await ctx.close();
      holder.srcObject = null;
      return Math.round(peak * 10000) / 10000;
    },
    webrtc() {
      return window.__e2e.comp('app-media')?.webRtcService ?? window.__e2e.comp('app-userlist')?.webrtcService;
    },
    voip() {
      return window.__e2e.comp('app-media')?.voipService;
    },
    playerType() {
      return window.__e2e.comp('app-media')?.CurrentPlayerType;
    },
    members() {
      return (window.__e2e.comp('app-userlist')?.Members ?? []).map(m => ({name: m.username, id: m.connectionId, live: m.isstreaming}));
    },
  };
}

async function openUser({name, username, password}) {
  const context = await browser.createBrowserContext();
  const page = await context.newPage();
  page.on('pageerror', err => console.log(`   [${name} pageerror] ${err.message.split('\n')[0]}`));
  page.on('console', msg => {
    const text = msg.text();
    if (msg.type() === 'error' && !/favicon|Failed to load resource|youtube|twitch|mpegts\.js/i.test(text)) {
      console.log(`   [${name} console.error] ${text.slice(0, 220)}`);
    }
  });
  await page.evaluateOnNewDocument(pageHelpers);
  await page.goto(APP, {waitUntil: 'domcontentloaded'});
  if (username) {
    const status = await page.evaluate(async (u, p) => {
      const res = await fetch('/api/auth/login', {method: 'POST', headers: {'Content-Type': 'application/json'}, credentials: 'include', body: JSON.stringify({username: u, password: p})});
      return res.status;
    }, username, password);
    if (status !== 200) throw new Error(`browser login ${username}: ${status}`);
  }
  const user = {name, username, page, context};
  await enterRoom(user);
  return user;
}

async function enterRoom(user) {
  await user.page.goto(`${APP}/room/${ROOM}`, {waitUntil: 'domcontentloaded'});
  await waitFor(user, () => window.__e2e && window.__e2e.members().length > 0, 30000, `${user.name} joins the room`);
}

/** Polls a predicate inside the page until it is truthy; returns its value, or throws after the timeout. */
async function waitFor(user, predicate, timeoutMs, what, ...args) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await user.page.evaluate(predicate, ...args);
      if (last) return last;
    } catch (err) {
      last = err.message;
    }
    await sleep(300);
  }
  throw new Error(`timed out waiting for: ${what} (last: ${JSON.stringify(last)})`);
}

/** Like waitFor but returns false instead of throwing. */
async function eventually(user, predicate, timeoutMs, ...args) {
  try {
    return await waitFor(user, predicate, timeoutMs, 'condition', ...args);
  } catch {
    return false;
  }
}

async function close() {
  fixtureServer?.close();
  await browser?.close();
  publishers.forEach(stopPublisher);
}

// ─── Live streaming ─────────────────────────────────────────────────────────

function setStreamToken(username, token) {
  execFileSync('docker', ['exec', 'dtest-db-1', 'psql', '-U', 'postgres', '-d', 'syncstream', '-c',
    `UPDATE "Users" SET "StreamToken" = '${token}' WHERE username = '${username}'`]);
}

const publishers = new Set();

/** Stands in for OBS: a test picture and a tone published to the streaming server. */
function publish(stream, token, frequency) {
  const child = spawn('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-re',
    '-f', 'lavfi', '-i', 'testsrc=s=640x360:r=25', '-f', 'lavfi', '-i', `sine=frequency=${frequency}:sample_rate=48000`,
    '-c:v', 'libx264', '-preset', 'veryfast', '-tune', 'zerolatency', '-g', '50', '-pix_fmt', 'yuv420p',
    '-c:a', 'aac', '-b:a', '96k', '-f', 'flv', `rtmp://127.0.0.1:1935/live/${stream}?token=${token}`],
    {stdio: ['ignore', 'ignore', 'pipe']});
  child.stderr.on('data', data => console.log(`   [obs ${stream}] ${data.toString().trim().slice(0, 200)}`));
  publishers.add(child);
  child.on('exit', () => publishers.delete(child));
  return child;
}

/**
 * Stops a publisher. On Windows ffmpeg is often a package manager's shim that starts the real binary as a child,
 * and killing the shim leaves the real one streaming, so the whole tree goes.
 */
function stopPublisher(child) {
  if (child.exitCode !== null) return;
  if (process.platform === 'win32') execFileSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], {stdio: 'ignore'});
  else child.kill('SIGINT');
}

/** Whether a URL serves an FLV stream: answers 200 and starts with the FLV signature within a few seconds. */
async function flvProbe(url, ms = 5000) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), ms);
  try {
    const res = await fetch(url, {signal: controller.signal});
    if (res.status !== 200) return {status: res.status, flv: false};
    const {value} = await res.body.getReader().read();
    return {status: 200, flv: !!value && Buffer.from(value.slice(0, 3)).toString() === 'FLV'};
  } catch (err) {
    return {status: err.name, flv: false};
  } finally {
    clearTimeout(timer);
    controller.abort();
  }
}

// ─── Room uploads from Node ─────────────────────────────────────────────────

async function startUpload(cookie, name, ending, totalSize) {
  const params = new URLSearchParams({uniqueId: ROOM, name, fileEnding: ending, totalSize: String(totalSize), skipPlaylist: 'true'});
  const res = await fetch(`${API}/api/video/roomUpload/start?${params}`, {method: 'POST', headers: {Cookie: cookie}});
  const body = await res.json();
  if (!body.uploadId) throw new Error(`upload start: ${res.status} ${JSON.stringify(body)}`);
  return body.uploadId;
}

async function uploadChunk(cookie, uploadId, buffer, from, to) {
  const params = new URLSearchParams({uploadId, startByte: String(from)});
  const res = await fetch(`${API}/api/video/roomUpload/chunk?${params}`, {
    method: 'PUT', headers: {Cookie: cookie, 'Content-Type': 'application/octet-stream'}, body: buffer.subarray(from, to)
  });
  if (res.status !== 200) throw new Error(`chunk ${from}-${to}: ${res.status} ${await res.text()}`);
}

module.exports = {
  APP, API, ROOM, sleep, check, scenario, results, register, login, hubClient, refusal, serveFixtures,
  launch, openUser, enterRoom, waitFor, eventually, close, setStreamToken, publish, stopPublisher, flvProbe, startUpload,
  uploadChunk,
};
