'use strict';
// End-to-end checks of room streaming, in one command:
//
//   cd tests/e2e
//   npm install
//   node run.js                     every scenario
//   node run.js rtmp obs            only scenarios whose names start with these
//
// Needs Docker, ffmpeg on PATH and Chrome (CHROME_PATH when it is not at the Windows default), and the Angular
// app's working tree (SYNCSTREAM_FRONTEND, defaulting to the same path verify.ps1 uses). It generates the media
// fixtures if they are missing, builds and starts the stack in docker-compose.yml from this working tree, serves
// the app on :4300 with the API behind a proxy whose connections can be cut, runs the scenarios in e2e.js, and
// takes it all down again. KEEP_STACK=1 leaves the containers running.
const {spawn, execFileSync} = require('child_process');
const path = require('path');

const frontend = process.env.SYNCSTREAM_FRONTEND || 'C:\\Angular Projects\\Drecktube Angular';
const windows = process.platform === 'win32';
const children = [];

function compose(...args) {
  execFileSync('docker', ['compose', '-f', path.join(__dirname, 'docker-compose.yml'), ...args], {stdio: 'inherit'});
}

/** Starts a long-running helper and resolves once its output shows it is ready. */
function startUntil(command, args, options, readyPattern, timeoutMs) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {...options, stdio: ['ignore', 'pipe', 'pipe']});
    children.push(child);
    let output = '';
    const timer = setTimeout(() => reject(new Error(`${command} was not ready after ${timeoutMs} ms:\n${output.slice(-2000)}`)), timeoutMs);
    const onData = data => {
      output += data.toString();
      if (readyPattern.test(output)) {
        clearTimeout(timer);
        resolve(child);
      }
    };
    child.stdout.on('data', onData);
    child.stderr.on('data', onData);
    child.on('exit', code => {
      clearTimeout(timer);
      reject(new Error(`${command} exited with ${code}:\n${output.slice(-2000)}`));
    });
  });
}

async function waitForApi(timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      await fetch('http://127.0.0.1:5000/api/rtmp/onplay', {method: 'POST'});
      return;
    } catch {
      await new Promise(resolve => setTimeout(resolve, 1000));
    }
  }
  throw new Error('the API did not answer');
}

function stop(child) {
  if (child.exitCode !== null) return;
  // npx and ng run as process trees on Windows; killing the top leaves the server listening.
  if (windows) execFileSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], {stdio: 'ignore'});
  else child.kill();
}

async function main() {
  require('./make-fixtures');
  compose('up', '-d', '--build');
  await waitForApi(120000);

  await startUntil(process.execPath, [path.join(__dirname, 'tcp-proxy.js')], {}, /tcp proxy/, 10000);
  const proxyConfig = path.join(__dirname, 'proxy.conf.json');
  await startUntil('npx', ['--no-install', 'ng', 'serve', '--port', '4300', '--proxy-config', `"${proxyConfig}"`],
    {cwd: frontend, shell: windows}, /Compiled successfully/, 300000);

  const scenarios = spawn(process.execPath, [path.join(__dirname, 'e2e.js'), ...process.argv.slice(2)], {stdio: 'inherit'});
  return new Promise(resolve => scenarios.on('exit', code => resolve(code ?? 1)));
}

main()
  .then(code => { process.exitCode = code; })
  .catch(err => {
    console.error(err);
    process.exitCode = 1;
  })
  .finally(() => {
    children.forEach(stop);
    if (!process.env.KEEP_STACK) compose('down', '-v');
  });
