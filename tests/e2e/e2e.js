'use strict';
const fs = require('fs');
const path = require('path');
const signalR = require('@microsoft/signalr');
const L = require('./lib');

const PASSWORD = 'E2e-Long-Password-2026';
const only = process.argv.slice(2);
const wants = name => only.length === 0 || only.some(prefix => name.startsWith(prefix));

// ─── Page-side probes, run with page.evaluate ────────────────────────────────

/** Frames and audio level of what the stage shows this user. */
async function stageReception() {
  const stream = window.__e2e.webrtc().remoteStream.value;
  const video = await window.__e2e.frames(stream, 2000);
  const rms = await window.__e2e.rms(stream?.getAudioTracks()[0] ?? null);
  return {...video, rms, playerType: window.__e2e.playerType(), state: window.__e2e.webrtc().stageState.value};
}

async function sendingLayers() {
  const sender = window.__e2e.webrtc().sender;
  if (!sender) return null;
  const producer = sender.producers.get('video')?.producer;
  if (!producer) return null;
  const report = await producer.getStats();
  const layers = [];
  report.forEach(stat => {
    if (stat.type === 'outbound-rtp' && stat.kind === 'video') layers.push(`${stat.rid ?? '-'}:${stat.frameHeight ?? '?'}p`);
  });
  return {codec: producer.rtpParameters.codecs[0].mimeType, layers: layers.sort()};
}

// ─── Scenarios ──────────────────────────────────────────────────────────────

async function stageViaDialog({alice, bob, guest}) {
  // The real flow: the user list's Go Live button, the dialog's three steps, the room as audience, with the
  // screen's audio and the microphone mixed at a volume that forces the mixer.
  await alice.page.evaluate(async () => {
    const userlist = window.__e2e.comp('app-userlist');
    userlist.goLive();
    window.ng.applyChanges(userlist);
    await window.__e2e.sleep(300);
    const dialog = window.__e2e.comp('app-stream-start-dialog');
    dialog.selectAudience('room');
    dialog.shareSystemAudio = true;
    dialog.selectSource('__display__');
    await dialog.next();   // capture
    await dialog.next();   // quality
    dialog.shareMicrophone = true;
    dialog.microphoneVolume = 80;
    await dialog.next();   // confirm
  });

  const dialogGone = await L.eventually(alice, () => !document.querySelector('app-stream-start-dialog'), 5000);
  L.check('the dialog closes on confirm', dialogGone === true);
  const live = await L.eventually(alice, () => window.__e2e.webrtc().localAudience.value === 'room', 15000);
  L.check('alice is live on the room stage', live === true);

  for (const viewer of [bob, guest]) {
    const shown = await L.eventually(viewer, () => !!window.__e2e.webrtc().remoteStream.value, 15000);
    L.check(`${viewer.name} receives the stage`, shown === true);
    await L.sleep(1500);
    const reception = await viewer.page.evaluate(stageReception);
    L.check(`${viewer.name} sees moving video`, reception.frames >= 20 && reception.width > 0, reception);
    // The mix used to go silent the moment the dialog closed; it has been closed for a while now.
    L.check(`${viewer.name} hears the mixed audio after the dialog closed`, reception.rms > 0.01, reception.rms);
    L.check(`${viewer.name}'s player shows the stage`, reception.playerType === 9, reception.playerType);
  }

  // The fake screen is 800x450, below the 540p where a second layer starts: one layer is right here.
  const layers = await alice.page.evaluate(sendingLayers);
  L.check('a 450p screen goes out as a single H.264 layer', layers && /h264/i.test(layers.codec) && layers.layers.length === 1, layers);
}

/** Starts a broadcast from a 1280x720 canvas and a tone, through the service the dialog hands over to. */
async function startCanvasBroadcast(audience) {
  const canvas = Object.assign(document.createElement('canvas'), {width: 1280, height: 720});
  const g = canvas.getContext('2d');
  let n = 0;
  const timer = setInterval(() => {
    g.fillStyle = `hsl(${(n++ * 5) % 360},70%,45%)`;
    g.fillRect(0, 0, 1280, 720);
    g.fillStyle = '#fff';
    g.fillRect((n * 9) % 1200, 300, 80, 80);
  }, 33);
  const video = canvas.captureStream(30).getVideoTracks()[0];
  const ctx = new AudioContext();
  const osc = ctx.createOscillator();
  const dest = ctx.createMediaStreamDestination();
  osc.connect(dest);
  osc.start();
  const audio = dest.stream.getAudioTracks()[0];
  const flag = window.__e2eCapture = {stopped: false};
  const capture = {
    stream: new MediaStream([video, audio]),
    encoding: {maxBitrate: 2_500_000, maxHeight: 720, frameRate: 30, contentPreference: 'motion'},
    release: () => {
      flag.stopped = true;
      clearInterval(timer);
      video.stop();
      audio.stop();
      ctx.close();
    }
  };
  await window.__e2e.webrtc().startLive('e2e', capture, audience);
  return window.__e2e.webrtc().localAudience.value;
}

async function stageEndings(users) {
  const {alice, bob, guest, openAlice} = users;

  // An explicit stop. The dialog broadcast from the previous scenario is still on.
  await alice.page.evaluate(() => window.__e2e.webrtc().stopOwnStream());
  for (const viewer of [bob, guest]) {
    const back = await L.eventually(viewer, () => !window.__e2e.webrtc().remoteStream.value && window.__e2e.playerType() !== 9, 8000);
    L.check(`stopping: ${viewer.name}'s player leaves the stage`, back === true);
  }

  // A 720p broadcast is simulcast.
  L.check('broadcast from a 720p source', (await alice.page.evaluate(startCanvasBroadcast, 'room')) === 'room');
  await L.eventually(bob, () => !!window.__e2e.webrtc().remoteStream.value, 10000);
  await L.sleep(3000);
  const layers = await alice.page.evaluate(sendingLayers);
  L.check('a 720p broadcast goes out as two simulcast layers', layers && layers.layers.length === 2, layers);

  // The broadcaster's tab closes: the room must not stay on a dead stage.
  await alice.context.close();
  for (const viewer of [bob, guest]) {
    const back = await L.eventually(viewer, () => !window.__e2e.webrtc().remoteStream.value && window.__e2e.playerType() !== 9, 15000);
    L.check(`disconnect: ${viewer.name}'s player leaves the dead stage`, back === true);
  }
  users.alice = await openAlice();

  // The broadcaster leaves the room through the UI.
  await users.alice.page.evaluate(startCanvasBroadcast, 'room');
  await L.eventually(bob, () => !!window.__e2e.webrtc().remoteStream.value, 10000);
  await users.alice.page.evaluate(() => window.__e2e.comp('app-rooms').LeaveRoom());
  const released = await L.eventually(users.alice, () => window.__e2eCapture.stopped === true, 8000);
  L.check('leaving: the broadcaster\'s capture is released', released === true);
  for (const viewer of [bob, guest]) {
    const back = await L.eventually(viewer, () => !window.__e2e.webrtc().remoteStream.value && window.__e2e.playerType() !== 9, 10000);
    L.check(`leaving: ${viewer.name}'s player leaves the stage`, back === true);
  }
  await L.enterRoom(users.alice);
}

async function liveInPopout(users) {
  const {alice, bob, guest} = users;
  L.check('alice goes live in the member list', (await alice.page.evaluate(startCanvasBroadcast, 'personal')) === 'personal');
  const aliceId = await L.waitFor(bob, () => window.__e2e.members().find(m => m.name === 'alice' && m.live)?.id, 8000, 'alice shown live');
  L.check('the member list shows alice live', !!aliceId);
  L.check('nobody is switched to a member-list stream', (await bob.page.evaluate(() => window.__e2e.playerType())) !== 9);

  for (const viewer of [bob, guest]) {
    await viewer.page.evaluate(id => window.__e2e.webrtc().startWatching('e2e', id, 'alice'), aliceId);
    const shown = await L.eventually(viewer, () => !!window.__e2e.webrtc().popoutStream.value, 10000);
    L.check(`${viewer.name} watches alice in the popout`, shown === true);
    await L.sleep(1500);
    const reception = await viewer.page.evaluate(async () => {
      const stream = window.__e2e.webrtc().popoutStream.value;
      return {...await window.__e2e.frames(stream, 2000), rms: await window.__e2e.rms(stream?.getAudioTracks()[0])};
    });
    L.check(`${viewer.name} gets video and audio in the popout`, reception.frames >= 20 && reception.rms > 0.05, reception);
  }

  await alice.page.evaluate(() => window.__e2e.webrtc().stopOwnStream());
  for (const viewer of [bob, guest]) {
    const closed = await L.eventually(viewer, () => window.__e2e.webrtc().watchRequest.value === null, 8000);
    L.check(`${viewer.name}'s popout closes when alice stops`, closed === true);
  }
  const notLive = await L.eventually(bob, () => !window.__e2e.members().find(m => m.name === 'alice')?.live, 5000);
  L.check('alice is no longer shown live', notLive === true);
}

async function voiceCall(users) {
  const {alice, bob, mallory} = users;
  await joinAsMallory(users);
  for (const user of [alice, bob]) await user.page.evaluate(() => window.__e2e.voip().join('e2e'));
  const ids = {};
  for (const [user, other] of [[alice, 'bob'], [bob, 'alice']]) {
    const joined = await L.eventually(user, name => {
      const voip = window.__e2e.voip();
      return voip.local.value && voip.participants.value.some(p => p.participantName === name);
    }, 15000, other);
    L.check(`${user.name} sees ${other} in the call`, joined === true);
  }
  ids.alice = await bob.page.evaluate(() => window.__e2e.voip().participants.value.find(p => p.participantName === 'alice').participantId);

  const heard = () => {
    const voip = window.__e2e.voip();
    const id = voip.participants.value.find(p => p.participantName === 'alice').participantId;
    const element = voip.audioElements.get(id);
    return window.__e2e.rms(element?.srcObject?.getAudioTracks()[0] ?? null, 2500);
  };
  await L.sleep(2000);
  const level = await bob.page.evaluate(heard);
  L.check('bob hears alice through the SFU', level > 0.01, level);

  // An outsider who is a member of the room still cannot get into the call's media room or its audio.
  L.check('a member outside the call cannot receive it',
    (await L.refusal(mallory.invoke('JoinSfuRoom', `voice:${L.ROOM}`))) === 'Join the call first.');

  await alice.page.evaluate(() => window.__e2e.voip().setMuted(true));
  await L.sleep(1500);
  const muted = await bob.page.evaluate(heard);
  const statusSeen = await bob.page.evaluate(() => window.__e2e.voip().participants.value.find(p => p.participantName === 'alice').isMuted);
  L.check('muting alice silences her for bob', muted < 0.005, muted);
  L.check("bob sees alice's mute", statusSeen === true);
  await alice.page.evaluate(() => window.__e2e.voip().setMuted(false));

  await alice.page.evaluate(() => window.__e2e.voip().setVideo('webcam'));
  const camera = await L.eventually(bob, () => !!window.__e2e.voip().participants.value.find(p => p.participantName === 'alice')?.stream, 10000);
  L.check("alice's camera reaches bob", camera === true);
  // The stream exists before its first keyframe has arrived, and the SFU spaces keyframe requests a second apart,
  // so the picture can take a moment to start. What matters is that it starts and keeps moving.
  const startedAt = Date.now();
  const cameraMoving = await L.eventually(bob, async () => {
    const sample = await window.__e2e.frames(window.__e2e.voip().participants.value.find(p => p.participantName === 'alice').stream, 2000);
    return sample.frames >= 20 ? sample : null;
  }, 10000);
  L.check("alice's camera is moving video within a few seconds", !!cameraMoving,
    {...cameraMoving, startedAfterMs: Date.now() - startedAt});

  // A game takes the room's screen; the call goes on.
  await mallory.invoke('PlayBlackjack', L.ROOM);
  const inGame = await L.eventually(bob, () => window.__e2e.playerType() === 7, 8000);
  L.check('the room switches to Blackjack', inGame === true);
  await L.sleep(1500);
  const duringGame = await bob.page.evaluate(heard);
  L.check('the call carries on during the game', duringGame > 0.01, duringGame);
  await mallory.invoke('PlayBlackjack', L.ROOM);
  await L.eventually(bob, () => window.__e2e.playerType() !== 7, 8000);

  await alice.page.evaluate(() => window.__e2e.voip().leave());
  const left = await L.eventually(bob, () => !window.__e2e.voip().participants.value.some(p => p.participantName === 'alice'), 8000);
  L.check('bob sees alice leave the call', left === true);
  await bob.page.evaluate(() => window.__e2e.voip().leave());
}

async function browserFileShare(users) {
  const {alice, bob} = users;
  await alice.page.evaluate(async () => {
    const blob = await (await fetch('http://localhost:4400/browser.mp4')).blob();
    window.__e2e.webrtc().requestFileShareHostUi(new File([blob], 'browser.mp4', {type: 'video/mp4'}));
  });
  const shared = await L.eventually(bob, () => !!window.__e2e.webrtc().fileShareActive.value && !!window.__e2e.webrtc().remoteStream.value, 20000);
  L.check('a file the browser plays is shared to the stage', shared === true);
  const reception = await bob.page.evaluate(stageReception);
  L.check('bob gets its video and audio', reception.frames >= 20 && reception.rms > 0.01 && reception.playerType === 10, reception);

  await alice.page.evaluate(() => window.__e2e.webrtc().stopFileShare('e2e'));
  const ended = await L.eventually(bob, () => !window.__e2e.webrtc().fileShareActive.value && window.__e2e.playerType() !== 10, 8000);
  L.check('stopping the share puts bob\'s player back', ended === true);
}

async function serverFileShare(users) {
  const {alice, bob} = users;
  // An MKV, which the browser cannot capture, so it is uploaded and the server streams it. It has no audio track,
  // which made ffmpeg refuse to start at all.
  await alice.page.evaluate(async () => {
    const blob = await (await fetch('http://localhost:4400/noaudio.mkv')).blob();
    window.__e2e.webrtc().requestFileShareHostUi(new File([blob], 'noaudio.mkv'));
  });
  const live = await L.eventually(bob, () => !!window.__e2e.webrtc().remoteStream.value, 30000);
  L.check('an audio-less MKV streams from the server', live === true);
  const reception = await bob.page.evaluate(stageReception);
  L.check('bob sees it move', reception.frames >= 20, reception);

  // Cut every connection while the rest of the file is still uploading. The chunk in flight is left half-written;
  // the upload has to pick up from where the server got to rather than give up.
  await L.sleep(300);
  const cut = await fetch('http://127.0.0.1:5002/cut', {method: 'POST'}).then(r => r.text());
  L.check('connections were cut mid-upload', Number(cut) > 0, cut);
  const uploaded = await L.eventually(alice, () => {
    const webrtc = window.__e2e.webrtc();
    return webrtc.fileShareUploadProgress.value === 100 && !webrtc.backgroundUploadInProgress.value;
  }, 60000);
  L.check('the upload completes after the cut', uploaded === true);

  const ended = await L.eventually(bob, () => !window.__e2e.webrtc().fileShareActive.value && window.__e2e.playerType() !== 10, 40000);
  L.check('the share ends by itself when the file has played out', ended === true);
  const hostReset = await L.eventually(alice, () => !window.__e2e.webrtc().isFileShareHost, 5000);
  L.check("the sharer's side is reset too", hostReset === true);
}

async function recording(users) {
  const {alice, bob} = users;
  await alice.page.evaluate(startCanvasBroadcast, 'room');
  await L.eventually(bob, () => !!window.__e2e.webrtc().remoteStream.value, 10000);
  await L.sleep(1500);
  await alice.page.evaluate(() => {
    window.__e2eSaved = null;
    window.__e2e.webrtc().recordingSaved.subscribe(file => window.__e2eSaved = file);
    return window.__e2e.webrtc().startRecording('e2e');
  });
  const recSeen = await L.eventually(bob, () => !!window.__e2e.webrtc().sfuRecording.value, 5000);
  L.check('viewers see the recording indicator', recSeen === true);
  await L.sleep(5000);
  await alice.page.evaluate(() => window.__e2e.webrtc().stopRecording('e2e'));
  const file = await L.waitFor(alice, () => window.__e2eSaved, 10000, 'recording saved');
  L.check('the broadcaster is told where the recording is', /\.mkv$/.test(file), file);
  const probe = require('child_process').execFileSync('docker', ['exec', 'dtest-sfu-1', 'ffprobe', '-v', 'error',
    '-show_entries', 'stream=codec_type:format=duration', '-of', 'csv=p=0', `/recordings/${file}`]).toString().trim().split(/\s+/);
  // Recorded for 5 seconds; the start costs at most a keyframe interval.
  L.check('the recording holds video and audio of the right length',
    probe.includes('video') && probe.includes('audio') && Number(probe.find(v => /^\d+\.\d+$/.test(v))) > 3.5, probe);
  const download = await alice.page.evaluate(async f => (await fetch(`/api/recordings/${encodeURIComponent(f)}`)).status, file);
  L.check('the broadcaster can download it', download === 200, download);
  await alice.page.evaluate(() => window.__e2e.webrtc().stopOwnStream());
  await L.eventually(bob, () => window.__e2e.playerType() !== 9, 8000);
}

async function sfuErrorsReachTheUser(users) {
  const {mallory, bob} = users;
  await joinAsMallory(users);
  // A stage held with nothing on it: the SFU refuses to record it, and that refusal arrives as its own words
  // rather than as "An unexpected error occurred".
  await mallory.invoke('StartSfuStream', L.ROOM);
  const message = await L.refusal(mallory.invoke('StartRecording', L.ROOM));
  L.check('an SFU refusal reaches the client as a readable message', message === 'No consumable producers in room', message);
  await mallory.invoke('StopSfuStream', L.ROOM);
  const back = await L.eventually(bob, () => window.__e2e.playerType() !== 9, 8000);
  L.check('the empty stage is released', back === true);
}

async function reconnects(users) {
  const {alice, bob} = users;
  await alice.page.evaluate(startCanvasBroadcast, 'room');
  await L.eventually(bob, () => !!window.__e2e.webrtc().remoteStream.value, 10000);

  // A network blip: every socket is cut, and the clients come back on new connections. The server has ended
  // everything the old ones had; the broadcaster starts it again, and the viewers find it again.
  const aliceIdBefore = (await bob.page.evaluate(() => window.__e2e.members())).find(m => m.name === 'alice').id;
  await fetch('http://127.0.0.1:5002/cut', {method: 'POST'});
  const aliceIdAfterBlip = await L.waitFor(bob, old => {
    const id = window.__e2e.members().find(m => m.name === 'alice')?.id;
    return id && id !== old ? id : null;
  }, 30000, 'alice back after the blip', aliceIdBefore);
  const backAfterBlip = await L.eventually(bob, () => !!window.__e2e.webrtc().remoteStream.value && window.__e2e.playerType() === 9, 20000);
  L.check('after a blip the broadcast comes back by itself', backAfterBlip === true);
  if (backAfterBlip) {
    await L.sleep(2000);
    const afterBlip = await bob.page.evaluate(stageReception);
    L.check('and plays', afterBlip.frames >= 20, afterBlip);
  }

  // A real reconnect: a brand-new connection. The server ends everything the old one had; the client starts it again.
  await alice.page.evaluate(() => window.__e2e.comp('app-rooms').signalRService.restartConnection());
  const newId = await L.waitFor(bob, old => {
    const id = window.__e2e.members().find(m => m.name === 'alice')?.id;
    return id && id !== old ? id : null;
  }, 20000, 'alice reconnects with a new connection', aliceIdAfterBlip);
  L.check('alice comes back on a new connection', !!newId);
  const resumed = await L.eventually(bob, () => !!window.__e2e.webrtc().remoteStream.value && window.__e2e.playerType() === 9, 20000);
  L.check("alice's broadcast resumes by itself after the new connection", resumed === true);
  if (resumed) {
    await L.sleep(2000);
    const afterReconnect = await bob.page.evaluate(stageReception);
    L.check('and bob sees it moving again', afterReconnect.frames >= 20, afterReconnect);
  }
  const sameCapture = await alice.page.evaluate(() => window.__e2eCapture.stopped === false);
  L.check('without asking alice to pick her screen again', sameCapture === true);
}

async function voiceAcrossReconnect({alice, bob}) {
  for (const user of [alice, bob]) await user.page.evaluate(() => window.__e2e.voip().join('e2e'));
  await L.eventually(bob, () => window.__e2e.voip().participants.value.some(p => p.participantName === 'alice'), 15000);
  await alice.page.evaluate(() => window.__e2e.voip().setMuted(true));
  await L.sleep(500);

  await alice.page.evaluate(() => window.__e2e.comp('app-rooms').signalRService.restartConnection());
  const back = await L.eventually(bob, () => {
    const alicePart = window.__e2e.voip().participants.value.find(p => p.participantName === 'alice');
    return !!alicePart && alicePart.isMuted === true;
  }, 25000);
  L.check('after a new connection alice is back in the call, still muted', back === true);

  await alice.page.evaluate(() => window.__e2e.voip().setMuted(false));
  await L.sleep(2500);
  const level = await bob.page.evaluate(() => {
    const voip = window.__e2e.voip();
    const id = voip.participants.value.find(p => p.participantName === 'alice').participantId;
    return window.__e2e.rms(voip.audioElements.get(id)?.srcObject?.getAudioTracks()[0] ?? null, 2500);
  });
  L.check('and bob hears her again', level > 0.01, level);
  for (const user of [alice, bob]) await user.page.evaluate(() => window.__e2e.voip().leave());
}

async function sfuRestart(users) {
  const {alice, bob, guest} = users;
  if (!(await alice.page.evaluate(() => window.__e2e.webrtc().localAudience.value === 'room'))) {
    await alice.page.evaluate(startCanvasBroadcast, 'room');
  }
  await L.eventually(bob, () => !!window.__e2e.webrtc().remoteStream.value, 10000);
  require('child_process').execFileSync('docker', ['restart', 'dtest-sfu-1']);
  for (const viewer of [bob, guest]) {
    const healed = await L.eventually(viewer, async () => {
      const stream = window.__e2e.webrtc().remoteStream.value;
      if (!stream) return false;
      const sample = await window.__e2e.frames(stream, 1000);
      return sample.frames >= 10;
    }, 60000);
    L.check(`after an SFU restart ${viewer.name} sees the broadcast again`, healed === true);
  }
  const sending = await alice.page.evaluate(() => window.__e2e.webrtc().localState.value);
  L.check('and the broadcaster is connected again', sending === 'connected', sending);
  await alice.page.evaluate(() => window.__e2e.webrtc().stopOwnStream());
}

/** Puts the account that probes for holes into the room, once; some checks need it outside, some inside. */
async function joinAsMallory(users) {
  if (users.malloryJoined) return;
  await users.mallory.invoke('AddUser', 'mallory', L.ROOM, null);
  users.malloryJoined = true;
  await L.sleep(500);
}

async function attacks(users) {
  const {mallory, alice} = users;
  // Not in the room yet.
  L.check('a non-member cannot join the stage',
    (await L.refusal(mallory.invoke('JoinSfuRoom', `stage:${L.ROOM}`))) === 'You are not in this room.');
  L.check('a non-member cannot take the stage',
    (await L.refusal(mallory.invoke('StartSfuStream', L.ROOM))) === 'You are not in this room.');
  L.check('a non-member cannot join the call',
    (await L.refusal(mallory.invoke('JoinAudioRoom', L.ROOM))) === 'You are not in this room.');

  await joinAsMallory(users);

  const producers = await mallory.invoke('JoinSfuRoom', `stage:${L.ROOM}`);
  L.check('a member may watch the stage', producers.length >= 1, producers.map(p => p.kind));
  const victim = producers.find(p => p.kind === 'video');
  L.check("a member cannot close the broadcaster's producer",
    (await L.refusal(mallory.invoke('CloseProducer', `stage:${L.ROOM}`, victim.id))) === 'Unknown producer.');
  L.check('a member cannot open a sending transport on a stage they do not hold',
    (await L.refusal(mallory.invoke('CreateWebRtcTransport', `stage:${L.ROOM}`, 0))) === "You are not on this room's stage.");
  L.check('a member cannot produce on a transport that is not theirs',
    (await L.refusal(mallory.invoke('Produce', `stage:${L.ROOM}`, 'not-mine', 'video', {}))) === 'Unknown transport.');
  L.check('a member cannot take a stage someone holds',
    (await L.refusal(mallory.invoke('StartSfuStream', L.ROOM))) === 'Someone else is already broadcasting to this room.');
  L.check('a member cannot start a file share over a broadcast',
    (await L.refusal(mallory.invoke('StartFileShare', L.ROOM))) === 'Someone else is already broadcasting to this room.');
  L.check('a member outside the call cannot join its media room',
    (await L.refusal(mallory.invoke('JoinSfuRoom', `voice:${L.ROOM}`))) === 'Join the call first.');
  L.check('an id that names no kind of room is refused',
    (await L.refusal(mallory.invoke('JoinSfuRoom', L.ROOM))) === 'Unknown media room.');

  await L.sleep(1000);
  const still = await alice.page.evaluate(() => window.__e2e.webrtc().localState.value);
  L.check('the broadcast survives all of that', still === 'connected', still);
  await mallory.invoke('LeaveSfuRoom', `stage:${L.ROOM}`);
}

// ─── RTMP ───────────────────────────────────────────────────────────────────

/**
 * What a live player shows: frames over a couple of seconds, the dominant pitch of its sound, and how many
 * bright pixels there are where the fixture's subtitle sits (bottom centre).
 */
async function liveReception(selector) {
  const video = document.querySelector(`${selector} video`);
  if (!video) return {missing: true};
  let frames = 0;
  let counting = true;
  const tick = () => { if (!counting) return; frames++; video.requestVideoFrameCallback(tick); };
  video.requestVideoFrameCallback(tick);

  let pitch = 0;
  const audio = video.captureStream().getAudioTracks()[0];
  if (audio) {
    const ctx = new AudioContext();
    const analyser = ctx.createAnalyser();
    analyser.fftSize = 8192;
    ctx.createMediaStreamSource(new MediaStream([audio])).connect(analyser);
    await window.__e2e.sleep(2000);
    const bins = new Float32Array(analyser.frequencyBinCount);
    analyser.getFloatFrequencyData(bins);
    let best = 1;
    for (let i = 1; i < bins.length; i++) if (bins[i] > bins[best]) best = i;
    pitch = Math.round(best * ctx.sampleRate / analyser.fftSize);
    await ctx.close();
  } else {
    await window.__e2e.sleep(2000);
  }
  counting = false;

  const canvas = Object.assign(document.createElement('canvas'), {width: video.videoWidth, height: video.videoHeight});
  const g = canvas.getContext('2d');
  g.drawImage(video, 0, 0);
  const w = video.videoWidth, h = video.videoHeight;
  let lit = 0;
  if (w > 0) {
    const band = g.getImageData(Math.round(w * 0.15), Math.round(h * 0.78), Math.round(w * 0.7), Math.round(h * 0.2)).data;
    for (let i = 0; i < band.length; i += 4) if (band[i] > 200 && band[i + 1] > 200 && band[i + 2] > 200) lit++;
  }
  return {frames, width: w, height: h, pitch, lit};
}

const rtmpPlayer = () => window.__e2e.comp('app-rtmp-file-share-player');

async function rtmpShare(users) {
  const {alice, bob, guest} = users;

  // The real flow: the side nav hands the file over, the player asks for the track preferences, the sharer
  // confirms. The fixture's English track is the default one, but Japanese comes first in the preferences.
  await alice.page.evaluate(async () => {
    const blob = await (await fetch('http://localhost:4400/anime.mkv')).blob();
    window.__e2e.webrtc().requestRtmpFileShareHostUi(new File([blob], 'anime.mkv'));
  });
  const probe = await L.waitFor(alice, () => {
    const player = window.__e2e.comp('app-rtmp-file-share-player');
    return player?.showPreferenceDialog && !player.isProbingRtmpMedia && player.rtmpMediaProbe
      ? {audio: player.rtmpMediaProbe.audioTracks.length, subtitles: player.rtmpMediaProbe.subtitleTracks.length}
      : null;
  }, 20000, 'the track dialog');
  L.check("the sharer is shown the file's tracks", probe.audio === 2 && probe.subtitles === 1, probe);
  await alice.page.evaluate(() => window.__e2e.comp('app-rtmp-file-share-player').confirmHostShareSettings());

  for (const viewer of [bob, guest]) {
    const playing = await L.eventually(viewer, () => window.__e2e.comp('app-rtmp-file-share-player')?.isPlaying === true, 30000);
    L.check(`${viewer.name} plays the RTMP share`, playing === true);
    const r = await viewer.page.evaluate(liveReception, 'app-rtmp-file-share-player');
    L.check(`${viewer.name} gets moving 720p video`, r.frames >= 30 && r.height === 720, r);
    L.check(`${viewer.name} hears the Japanese track, not the default English one`, Math.abs(r.pitch - 440) < 15, r.pitch);
    L.check(`${viewer.name} sees no subtitle before it starts at 0:30`, r.lit < 150, r.lit);
  }
  L.check('the sharer watches it too',
    (await L.eventually(alice, () => window.__e2e.comp('app-rtmp-file-share-player')?.isPlaying === true, 20000)) === true);

  // Who may play it.
  const stream = await bob.page.evaluate(() => window.__e2e.webrtc().rtmpFileShareActive.value.streamName);
  L.check('the share has its own stream, not the sharer\'s OBS one', /^share-[0-9a-f]{32}$/.test(stream), stream);
  const outsider = await L.hubClient(null);
  L.check('someone outside the room gets no play URL',
    (await L.refusal(outsider.invoke('GetLivePlayUrl', L.ROOM, stream))) === 'You cannot watch that stream.');
  await outsider.stop();
  const bare = await L.flvProbe(`http://127.0.0.1:8088/live?port=1935&app=live&stream=${stream}`);
  L.check('the streaming server refuses a play without a ticket', !bare.flv, bare);
  const url = await bob.page.evaluate(s => window.__e2e.comp('app-rtmp-file-share-player').liveStreamService.playUrl('e2e', s), stream);
  const ticketed = await L.flvProbe(url);
  L.check('a play URL from the hub plays', ticketed.flv, ticketed);
  const elsewhere = await L.flvProbe(url.replace(/stream=[^&]+/, 'stream=alice'));
  L.check('its ticket does not play another stream', !elsewhere.flv, elsewhere);

  // Pause and resume, for everyone.
  await alice.page.evaluate(() => window.__e2e.comp('app-rtmp-file-share-player').togglePlayPause());
  const paused = await L.eventually(bob, () => {
    const player = window.__e2e.comp('app-rtmp-file-share-player');
    return player.isPaused && !player.isPlaying;
  }, 8000);
  L.check('pausing pauses everyone', paused === true);
  const pausedAt = await bob.page.evaluate(() => window.__e2e.comp('app-rtmp-file-share-player').position);
  await L.sleep(3000);
  const stillAt = await bob.page.evaluate(() => window.__e2e.comp('app-rtmp-file-share-player').position);
  L.check('the position holds while paused', Math.abs(stillAt - pausedAt) < 0.5, {pausedAt, stillAt});
  await alice.page.evaluate(() => window.__e2e.comp('app-rtmp-file-share-player').togglePlayPause());
  const resumed = await L.eventually(bob, () => {
    const player = window.__e2e.comp('app-rtmp-file-share-player');
    return player.isPlaying && !player.isPaused;
  }, 20000);
  L.check('resuming plays on for everyone', resumed === true);
  const resumedAt = await bob.page.evaluate(() => window.__e2e.comp('app-rtmp-file-share-player').position);
  L.check('from where it paused', resumedAt >= pausedAt - 1 && resumedAt < pausedAt + 8, {pausedAt, resumedAt});

  // Seeking, once the upload is done. The subtitle runs from 0:30; burning it into a run that starts at 0:40
  // used to place it by the run's own clock, which starts at zero.
  const seekable = await L.eventually(alice, () => window.__e2e.webrtc().rtmpFileShareCanSeek.value === true, 60000);
  L.check('seeking opens once the upload is complete', seekable === true);
  await alice.page.evaluate(() => window.__e2e.webrtc().broadcastRtmpFileShareSeek('e2e', 40));
  const atForty = await L.eventually(bob, () => {
    const player = window.__e2e.comp('app-rtmp-file-share-player');
    return player.isPlaying && player.position >= 40 && player.position < 50;
  }, 20000);
  L.check('a seek moves everyone there', atForty === true);
  await L.sleep(1500);
  const afterSeek = await bob.page.evaluate(liveReception, 'app-rtmp-file-share-player');
  L.check('subtitles are burned in at the right time after a seek', afterSeek.lit > 300, afterSeek);
  L.check('and the picture keeps moving', afterSeek.frames >= 30, afterSeek.frames);

  // The end of the file ends the share, once.
  await alice.page.evaluate(() => window.__e2e.webrtc().broadcastRtmpFileShareSeek('e2e', 84));
  const ended = await L.eventually(bob, () => !window.__e2e.webrtc().rtmpFileShareActive.value && window.__e2e.playerType() !== 11, 30000);
  L.check('the share ends by itself at the end of the file', ended === true);
  await L.sleep(5000);
  const restarted = await bob.page.evaluate(() => !!window.__e2e.webrtc().rtmpFileShareActive.value);
  L.check('and does not start over', restarted === false);
  L.check("the sharer's side is reset", (await alice.page.evaluate(() => window.__e2e.webrtc().isRtmpFileShareHost)) === false);
}

async function rtmpShareOutrunsUpload({bob}) {
  // Shared from Node so the upload can be held back: the first chunk, about 16 seconds of the file, then
  // nothing until playback has caught up.
  // Room uploads are for administrators.
  const cookie = await L.login('carol', PASSWORD);
  const sharer = await L.hubClient(cookie);
  await sharer.invoke('AddUser', 'carol', L.ROOM, null);
  const file = fs.readFileSync(path.join(__dirname, 'fixtures', 'anime.mkv'));
  const chunk = 8 * 1024 * 1024;
  const uploadId = await L.startUpload(cookie, 'anime', '.mkv', file.length);
  await L.uploadChunk(cookie, uploadId, file, 0, chunk);
  await sharer.invoke('StartRtmpFileShareFromUpload', L.ROOM, uploadId, null);

  const playing = await L.eventually(bob, () => window.__e2e.comp('app-rtmp-file-share-player')?.isPlaying === true, 30000);
  L.check('a share plays while its file is still uploading', playing === true);
  const caughtUp = await L.eventually(bob, () => window.__e2e.comp('app-rtmp-file-share-player')?.isBuffering === true, 45000);
  L.check('playback waits when it catches up with the upload', caughtUp === true);
  const waitedAt = await bob.page.evaluate(() => window.__e2e.comp('app-rtmp-file-share-player').position);
  L.check('having played what had arrived', waitedAt > 10, waitedAt);

  await L.uploadChunk(cookie, uploadId, file, chunk, 2 * chunk);
  await L.uploadChunk(cookie, uploadId, file, 2 * chunk, 3 * chunk);
  const goesOn = await L.eventually(bob, () => {
    const player = window.__e2e.comp('app-rtmp-file-share-player');
    return player.isPlaying && !player.isBuffering;
  }, 30000);
  L.check('it plays on once more has arrived', goesOn === true);
  const goneOnAt = await bob.page.evaluate(() => window.__e2e.comp('app-rtmp-file-share-player').position);
  L.check('from where it waited, not from the start', goneOnAt >= waitedAt - 1 && goneOnAt < waitedAt + 10, {waitedAt, goneOnAt});

  for (let from = 3 * chunk; from < file.length; from += chunk) {
    await L.uploadChunk(cookie, uploadId, file, from, Math.min(file.length, from + chunk));
  }
  const viaHttp = await fetch(`${L.API}/api/video/roomUpload/complete?uploadId=${uploadId}`, {method: 'POST', headers: {Cookie: cookie}});
  L.check("a share's upload cannot be finalised from under it", viaHttp.status === 409, viaHttp.status);
  await sharer.invoke('MarkRtmpFileShareUploadComplete', L.ROOM, uploadId);
  L.check('completing it through the share makes it seekable',
    (await L.eventually(bob, () => window.__e2e.webrtc().rtmpFileShareCanSeek.value === true, 5000)) === true);

  await sharer.stop();
  const ended = await L.eventually(bob, () => !window.__e2e.webrtc().rtmpFileShareActive.value, 15000);
  L.check("the share ends when the sharer's connection does", ended === true);
}

async function obsLive({alice, bob, guest}) {
  L.setStreamToken('alice', 'e2e-alice-key');
  L.setStreamToken('bob', 'e2e-bob-key');
  const watcher = await L.hubClient(await L.login('bob', PASSWORD));
  let liveList = [];
  watcher.on('getliveusers', list => liveList = list);
  const aliceObs = L.publish('alice', 'e2e-alice-key', 440);
  const bobObs = L.publish('bob', 'e2e-bob-key', 880);
  const listed = async names => {
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
      if (names.every(name => liveList.some(user => user.userName === name)) && liveList.length === names.length) return true;
      await L.sleep(300);
    }
    return false;
  };
  L.check('both OBS streams are listed live', await listed(['alice', 'bob']), liveList.map(u => u.userName));

  // One of them stops: only that one leaves the list. Taking the first entry instead of the one that stopped
  // used to drop whoever had gone live first.
  L.stopPublisher(bobObs);
  L.check('when bob stops, alice is still listed', await listed(['alice']), liveList.map(u => u.userName));

  const bare = await L.flvProbe('http://127.0.0.1:8088/live?port=1935&app=live&stream=alice');
  L.check('the streaming server refuses an OBS play without a ticket', !bare.flv, bare);

  // An account may watch any live stream, on the live page.
  const page = await bob.context.newPage();
  await page.evaluateOnNewDocument(() => { window.__e2e = {sleep: ms => new Promise(r => setTimeout(r, ms))}; });
  await page.goto(`${L.APP}/live/alice`, {waitUntil: 'domcontentloaded'});
  const livePage = {name: 'bob (live page)', page};
  const direct = await L.eventually(livePage, () => {
    const el = document.querySelector('app-mpegts-player');
    return el && window.ng.getComponent(el).isPlaying === true;
  }, 30000);
  L.check('an account watches an OBS stream on the live page', direct === true);
  if (direct) {
    const r = await page.evaluate(liveReception, 'app-mpegts-player');
    L.check('with its picture and sound', r.frames >= 30 && Math.abs(r.pitch - 440) < 15, r);
  }
  await page.close();

  // A guest only for what their room shows.
  const guestHub = await L.hubClient(null);
  L.check('a guest outside any room gets no play URL for an OBS stream',
    (await L.refusal(guestHub.invoke('GetLivePlayUrl', null, 'alice'))) === 'You cannot watch that stream.');
  await guestHub.stop();

  await alice.page.evaluate(() =>
    window.__e2e.comp('app-media').playlistService.addVideo({title: 'alice', url: 'https://live.drecktu.be/live?stream=alice'}, 'e2e'));
  const shown = await L.eventually(guest, () => {
    const el = document.querySelector('app-mpegts-player');
    return el && window.ng.getComponent(el).isPlaying === true;
  }, 30000);
  L.check("a guest watches the OBS stream their room shows", shown === true);
  if (shown) {
    const r = await guest.page.evaluate(liveReception, 'app-mpegts-player');
    L.check('with its picture and sound', r.frames >= 30 && Math.abs(r.pitch - 440) < 15, r);
  }

  L.stopPublisher(aliceObs);
  const offline = await L.eventually(guest, () => {
    const el = document.querySelector('app-mpegts-player');
    return el && window.ng.getComponent(el).offline === true;
  }, 30000);
  L.check("when the stream stops, the room's players say it is offline", offline === true);
  L.check('and the live list is empty', await listed([]), liveList.map(u => u.userName));
  await watcher.stop();
}

async function largeReplies() {
  // A call whose writes to its caller pass 100 KB, with the caller's next calls queued behind it. Under stateful
  // reconnect the server stopped writing until the caller acknowledged, but read nothing from the caller until the
  // call had finished, so the acknowledgement was never seen and the connection was dropped. A long playlist, which
  // the room list carries to everyone, was enough to disconnect whoever joined.
  const admin = await L.hubClient(await L.login('carol', PASSWORD));
  await admin.invoke('AddRoom', {uniqueId: 'bulk', name: 'Bulk', password: null, deletable: true});
  await admin.invoke('AddUser', 'carol', 'bulk', null);
  for (let i = 0; i < 14; i++) {
    await admin.invoke('AddVideo', {title: `Bulk ${i} ${'x'.repeat(9000)}`, url: `https://example.invalid/bulk/${i}.mp4`}, 'bulk');
  }

  // Asks for stateful reconnect the way the app did, so turning it back on in the server fails here.
  const joiner = new signalR.HubConnectionBuilder().withUrl(`${L.API}/server`).withStatefulReconnect()
    .configureLogging(signalR.LogLevel.None).build();
  let playlistChars = 0;
  let closed = null;
  joiner.on('playlistupdate', playlist => playlistChars = JSON.stringify(playlist).length);
  joiner.onclose(err => closed = err?.message ?? 'closed');
  await joiner.start();
  const calls = await Promise.allSettled([
    joiner.invoke('AddUser', 'Joiner', 'bulk', null), joiner.invoke('GetRooms'), joiner.invoke('GetRooms')]);
  await L.sleep(8000);
  const outcome = {playlistChars, calls: calls.map(call => call.status), closed, state: joiner.state};
  L.check('a reply over 100 KB with calls queued behind it keeps the connection',
    playlistChars > 100000 && calls.every(call => call.status === 'fulfilled') && closed === null && joiner.state === 'Connected', outcome);
  await joiner.stop();
  await admin.stop();
}

// ─── Main ───────────────────────────────────────────────────────────────────

(async () => {
  L.serveFixtures(path.join(__dirname, 'fixtures'));
  await L.register('alice', PASSWORD, 3);
  await L.register('bob', PASSWORD, 1);
  await L.register('mallory', PASSWORD, 1);
  await L.register('carol', PASSWORD, 3);
  const setup = await L.hubClient(await L.login('alice', PASSWORD));
  await setup.invoke('AddRoom', {uniqueId: L.ROOM, name: 'E2E', password: null, deletable: true});
  await setup.stop();

  await L.launch();
  const alice = await L.openUser({name: 'alice', username: 'alice', password: PASSWORD});
  const bob = await L.openUser({name: 'bob', username: 'bob', password: PASSWORD});
  const guest = await L.openUser({name: 'guest'});
  const mallory = await L.hubClient(await L.login('mallory', PASSWORD));
  const openAlice = () => L.openUser({name: 'alice', username: 'alice', password: PASSWORD});
  const users = {alice, bob, guest, mallory, openAlice};

  if (wants('stage')) await L.scenario('stage: broadcast through the dialog, with mixed audio', () => stageViaDialog(users));
  if (wants('attack')) await L.scenario('attacks against a live broadcast', () => attacks(users));
  if (wants('endings')) await L.scenario('stage: stopping, disconnecting and leaving', () => stageEndings(users));
  if (wants('live')) await L.scenario('member-list live stream in the popout', () => liveInPopout(users));
  if (wants('voice')) await L.scenario('voice call on the SFU', () => voiceCall(users));
  if (wants('browserfile')) await L.scenario('file share played by the browser', () => browserFileShare(users));
  if (wants('serverfile')) await L.scenario('file share streamed by the server', () => serverFileShare(users));
  if (wants('recording')) await L.scenario('recording the stage', () => recording(users));
  if (wants('errors')) await L.scenario('SFU errors reach the user', () => sfuErrorsReachTheUser(users));
  if (wants('reconnect')) await L.scenario('reconnecting', () => reconnects(users));
  if (wants('voicereconnect')) await L.scenario('voice call across a new connection', () => voiceAcrossReconnect(users));
  if (wants('sfurestart')) await L.scenario('SFU restart mid-broadcast', () => sfuRestart(users));
  if (wants('rtmpshare')) await L.scenario('RTMP file share through the UI', () => rtmpShare(users));
  if (wants('rtmpupload')) await L.scenario('RTMP file share outrunning its upload', () => rtmpShareOutrunsUpload(users));
  if (wants('obs')) await L.scenario('OBS live streams', () => obsLive(users));
  if (wants('large')) await L.scenario('large replies', () => largeReplies());

  const failed = L.results.filter(r => !r.ok).length;
  console.log(`\n${L.results.length - failed}/${L.results.length} checks passed`);
  await mallory.stop();
  await L.close();
  process.exit(failed === 0 ? 0 : 1);
})().catch(async err => {
  console.error(err);
  await L.close();
  process.exit(1);
});
