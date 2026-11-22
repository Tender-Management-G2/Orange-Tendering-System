// Replays recorded utterances through the browser speech front end (wwwroot/js/orange-radar-audio.js) as a 48 kHz microphone
// would deliver them (1024-sample worklet frames), and writes the 16 kHz WAVs the page would upload. The previous front end
// (box-filter resampler, 850 ms endpoint, no hysteresis, speech after the first endpoint ignored) is kept here only as the baseline.
// Usage: node stt-pipeline.js <corpus folder with manifest.json and raw/*.wav>   -> <corpus>/<pipeline>-<condition>/*.wav
'use strict';
const fs = require('fs'); const path = require('path');
const audio = require('../../wwwroot/js/orange-radar-audio.js');
const dir = process.argv[2]; const MIC = 48000; const FRAME = 1024;
const manifest = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json'), 'utf8'));

function readWav(file) {
  const b = fs.readFileSync(file); const rate = b.readUInt32LE(24); let p = 12;
  while (b.toString('ascii', p, p + 4) !== 'data') p += 8 + b.readUInt32LE(p + 4);
  const n = b.readUInt32LE(p + 4) / 2; const s = new Float32Array(n);
  for (let i = 0; i < n; i++) s[i] = b.readInt16LE(p + 8 + i * 2) / 32768;
  return { rate, samples: s };
}
let seed = 7; // mulberry32: deterministic, so every pipeline hears identical noise
const rand = () => { seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return (((t ^ (t >>> 14)) >>> 0) / 4294967296) * 2 - 1; };
function pinkNoise(n) {
  const out = new Float32Array(n); let b0 = 0, b1 = 0, b2 = 0, prevIn = 0, prevOut = 0;
  for (let i = 0; i < n; i++) {
    const w = rand(); b0 = 0.997 * b0 + w * 0.029591; b1 = 0.985 * b1 + w * 0.032534; b2 = 0.95 * b2 + w * 0.048056;
    const x = b0 + b1 + b2 + w * 0.05; prevOut = x - prevIn + 0.995 * prevOut; prevIn = x; out[i] = prevOut; // DC blocker
  }
  return out;
}
const rms = a => Math.sqrt(a.reduce((s, x) => s + x * x, 0) / a.length);
const conditions = {
  clean: { gain: 1, snr: 45 },
  noisy: { gain: 1, snr: 15 },  // fan / office background
  quiet: { gain: 0.12, snr: 30 } // distant or soft-spoken user
};
function microphone(raw, c) {
  const speech = audio.resample(raw.samples, raw.rate, MIC).map(x => x * c.gain);
  const lead = Math.round(MIC * 1.0); const total = new Float32Array(Math.max(lead + speech.length + Math.round(MIC * 1.5), MIC * 16)); // background continues, as in a room
  total.set(speech, lead);
  const voiced = speech.filter(x => Math.abs(x) > 0.01 * c.gain); const level = rms(voiced.length ? voiced : speech);
  const noise = pinkNoise(total.length); const scale = level / Math.pow(10, c.snr / 20) / rms(noise);
  for (let i = 0; i < total.length; i++) total[i] += noise[i] * scale;
  return total;
}
function frames(signal, fn) { for (let o = 0; o + FRAME <= signal.length; o += FRAME) { const f = signal.slice(o, o + FRAME); fn(f, rms(f), (o / MIC) * 1000); } }

// ---- previous front end (copied from orange-radar-voice.js before the overhaul) ----
function oldResample(input, from, to) {
  const ratio = from / to; const out = new Float32Array(Math.floor(input.length / ratio));
  for (let i = 0; i < out.length; i++) { const start = Math.floor(i * ratio), end = Math.min(input.length, Math.floor((i + 1) * ratio)); let sum = 0; for (let j = start; j < end; j++) sum += input[j]; out[i] = sum / Math.max(1, end - start); }
  return out;
}
function oldPipeline(signal) {
  const vad = { floor: 0.004, speaking: false, speechMs: 0, silenceMs: 0, chunks: [], pre: [], preMs: 0 }; let first = null; let ignoredVoicedMs = 0;
  frames(signal, (frame, r) => {
    const frameMs = (frame.length / MIC) * 1000;
    if (first) { if (r > 0.011) ignoredVoicedMs += frameMs; return; } // state is transcribing/analyzing: frames are dropped
    vad.pre.push(frame); vad.preMs += frameMs;
    while (vad.preMs > 360 && vad.pre.length > 1) vad.preMs -= (vad.pre.shift().length / MIC) * 1000;
    const threshold = Math.max(vad.floor * 3.2, 0.011);
    if (r > threshold) { vad.speechMs += frameMs; vad.silenceMs = 0; if (!vad.speaking && vad.speechMs >= 150) { vad.speaking = true; vad.chunks = [...vad.pre]; } }
    else { if (!vad.speaking) { vad.floor = Math.min(0.03, vad.floor * 0.97 + r * 0.03); vad.speechMs = Math.max(0, vad.speechMs - frameMs); } else vad.silenceMs += frameMs; }
    if (vad.speaking) {
      vad.chunks.push(frame);
      const total = vad.chunks.reduce((n, c) => n + c.length, 0) / MIC;
      if (vad.silenceMs >= 850 || total >= 15) { const n = vad.chunks.reduce((a, c) => a + c.length, 0); if (n / MIC >= 0.5) { first = new Float32Array(n); let o = 0; vad.chunks.forEach(c => { first.set(c, o); o += c.length; }); } vad.speaking = false; vad.chunks = []; vad.speechMs = 0; vad.silenceMs = 0; }
    }
  });
  return { samples: first ? oldResample(first, MIC, 16000) : null, segments: first ? 1 : 0, droppedVoicedMs: Math.round(ignoredVoicedMs) };
}

// ---- current front end: endpointer + continuation merge (speech resumed before the turn is sent joins the same utterance) ----
function newPipeline(signal) {
  const ep = audio.createEndpointer(); let held = null; let endedAt = null; let startedAt = null; let segments = 0;
  frames(signal, (frame, r, t) => {
    const ev = ep.push(frame, r, MIC);
    if (ev?.type === 'start') startedAt = t;
    if (ev?.type === 'end') {
      segments++;
      const part = audio.resample(ev.samples, MIC, 16000);
      if (held && startedAt - endedAt < 2500) { const joined = new Float32Array(held.length + 4800 + part.length); joined.set(held); joined.set(part, held.length + 4800); held = joined; }
      else if (!held) held = part;
      endedAt = t;
    }
  });
  return { samples: held, segments, droppedVoicedMs: 0 };
}

const report = [];
for (const [cname, c] of Object.entries(conditions)) for (const [pname, run] of [['old', oldPipeline], ['new', newPipeline]]) {
  const out = path.join(dir, `${pname}-${cname}`); fs.mkdirSync(out, { recursive: true });
  for (const item of manifest) {
    const signal = microphone(readWav(path.join(dir, 'raw', `${item.id}.wav`)), c);
    const started = process.hrtime.bigint(); const r = run(signal); const ms = Number(process.hrtime.bigint() - started) / 1e6;
    const file = path.join(out, `${item.id}.wav`);
    if (r.samples) fs.writeFileSync(file, Buffer.from(audio.encodeWav(r.samples))); else if (fs.existsSync(file)) fs.unlinkSync(file);
    report.push({ pipeline: pname, condition: cname, id: item.id, segments: r.segments, seconds: r.samples ? +(r.samples.length / 16000).toFixed(2) : 0, droppedVoicedMs: r.droppedVoicedMs, frontEndMs: +ms.toFixed(1) });
  }
}
fs.writeFileSync(path.join(dir, 'frontend.json'), JSON.stringify(report, null, 1));
const sum = (p, c, k) => report.filter(x => x.pipeline === p && x.condition === c).reduce((n, x) => n + (k === 'missing' ? (x.seconds ? 0 : 1) : x[k]), 0);
for (const c of Object.keys(conditions)) for (const p of ['old', 'new'])
  console.log(`${p}-${c}: missing=${sum(p, c, 'missing')} dropped speech after first endpoint=${(sum(p, c, 'droppedVoicedMs') / 1000).toFixed(1)} s, front end ${(sum(p, c, 'frontEndMs')).toFixed(0)} ms total`);
console.log('long-pauses:', report.filter(x => x.id === 'long-pauses').map(x => `${x.pipeline}-${x.condition} ${x.seconds}s/${x.segments}seg dropped ${x.droppedVoicedMs}ms`).join(' | '));
