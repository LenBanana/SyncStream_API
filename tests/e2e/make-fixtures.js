'use strict';
// Generates the media the scenarios share, with ffmpeg from PATH. Nothing here is checked in: run this once,
// or let run.js do it when a file is missing.
const {execFileSync} = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const dir = path.join(__dirname, 'fixtures');
fs.mkdirSync(dir, {recursive: true});

function ffmpeg(args) {
  execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', ...args], {stdio: 'inherit'});
}

function make(name, build) {
  const file = path.join(dir, name);
  if (fs.existsSync(file)) return;
  console.log(`making ${name}`);
  build(file);
}

// A file the browser plays itself, for the browser-side file share: 15 s of 720p H.264 and AAC.
make('browser.mp4', file => ffmpeg([
  '-f', 'lavfi', '-i', 'testsrc=s=1280x720:r=30:d=15', '-f', 'lavfi', '-i', 'sine=frequency=600:sample_rate=48000:d=15',
  '-c:v', 'libx264', '-preset', 'veryfast', '-b:v', '2800k', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '64k',
  '-movflags', '+faststart', file]));

// A file the server streams through the SFU: no audio track, which once made ffmpeg refuse to start, and a high
// bitrate so it takes several upload chunks and a connection can be cut in the middle of them.
make('noaudio.mkv', file => ffmpeg([
  '-f', 'lavfi', '-i', 'testsrc=s=1280x720:r=30:d=12',
  '-vf', 'noise=alls=40:allf=t', '-c:v', 'libx264', '-preset', 'veryfast', '-b:v', '20M', '-maxrate', '20M',
  '-bufsize', '20M', '-pix_fmt', 'yuv420p', file]));

// A file shaped like a typical anime release, for the RTMP share. A Japanese audio track at 440 Hz that is not the
// default and an English one at 1760 Hz that is, so the pitch a viewer hears tells which one the language
// preference chose. An ASS subtitle in white across the bottom from 0:30, so a frame grab shows whether it was
// burned in, and at the right time after a seek. Constant 4 Mbps, so 8 MB is about 16 seconds of it.
make('anime.mkv', file => {
  const subtitles = path.join(os.tmpdir(), 'syncstream-e2e-subs.ass');
  fs.writeFileSync(subtitles, [
    '[Script Info]', 'ScriptType: v4.00+', 'PlayResX: 1280', 'PlayResY: 720', '',
    '[V4+ Styles]',
    'Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding',
    'Style: Default,Arial,72,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,-1,0,0,0,100,100,0,0,1,2,0,2,10,10,40,1', '',
    '[Events]',
    'Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text',
    'Dialogue: 0,0:00:30.00,0:01:30.00,Default,,0,0,0,,SUBTITLES ARE BURNED IN', ''
  ].join('\n'));

  // Releases carry their fonts; attach one when this machine has a usable one, as the API's own fonts do otherwise.
  const font = ['C:/Windows/Fonts/arial.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', '/Library/Fonts/Arial.ttf']
    .find(candidate => fs.existsSync(candidate));
  const attach = font ? ['-attach', font, '-metadata:s:t:0', 'mimetype=application/x-truetype-font'] : [];

  ffmpeg([
    '-f', 'lavfi', '-i', 'color=c=black:s=1280x720:r=24:d=90', '-f', 'lavfi', '-i', 'testsrc=s=320x180:r=24:d=90',
    '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000:d=90',
    '-f', 'lavfi', '-i', 'sine=frequency=1760:sample_rate=48000:d=90',
    '-i', subtitles, ...attach,
    '-filter_complex', '[0:v][1:v]overlay=20:20,noise=alls=12:allf=t,format=yuv420p[v]',
    '-map', '[v]', '-map', '2:a', '-map', '3:a', '-map', '4:s',
    '-c:v', 'libx264', '-preset', 'veryfast', '-b:v', '4M', '-minrate', '4M', '-maxrate', '4M', '-bufsize', '8M',
    '-x264-params', 'nal-hrd=cbr', '-g', '48', '-c:a', 'aac', '-b:a', '128k', '-c:s', 'ass',
    '-metadata:s:a:0', 'language=jpn', '-metadata:s:a:1', 'language=eng',
    '-disposition:a:0', '0', '-disposition:a:1', 'default',
    '-metadata:s:s:0', 'language=eng', '-metadata:s:s:0', 'title=Full Subtitles',
    file]);
  fs.rmSync(subtitles);
});
