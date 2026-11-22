// ORANGE RADAR speech front end: endpointing, resampling to 16 kHz and WAV encoding for local Whisper.
// Pure functions (no DOM), loaded by the page before orange-radar-voice.js and by Tests/RadarChecks/stt-pipeline.js, so the
// benchmark measures exactly the code the browser runs.
(function (global) {
  'use strict';

  // Windowed-sinc (Blackman) resampler. The low-pass sits below the lower Nyquist frequency, so microphone energy above 8 kHz
  // cannot alias into the speech band (a plain average of neighbouring samples lets most of it through).
  const PHASES = 256; const kernelCache = new Map();
  function kernels(cutoff, half) {
    const key = `${cutoff}:${half}`; if (kernelCache.has(key)) return kernelCache.get(key);
    const taps = 2 * half; const table = [];
    for (let p = 0; p <= PHASES; p++) {
      const k = new Float32Array(taps); let sum = 0;
      for (let t = 0; t < taps; t++) {
        const x = t - half + 1 - p / PHASES; const a = Math.PI * x * cutoff;
        const u = (x + half) / (2 * half); const w = 0.42 - 0.5 * Math.cos(2 * Math.PI * u) + 0.08 * Math.cos(4 * Math.PI * u);
        k[t] = (a === 0 ? 1 : Math.sin(a) / a) * w; sum += k[t];
      }
      for (let t = 0; t < taps; t++) k[t] /= sum;
      table.push(k);
    }
    kernelCache.set(key, table); return table;
  }
  function resample(input, from, to) {
    if (from === to) return input;
    const ratio = from / to; const out = new Float32Array(Math.floor(input.length / ratio));
    const cutoff = 0.92 * Math.min(1, to / from); // normalised to the input rate's Nyquist
    const half = Math.ceil(12 / cutoff);           // 12 zero crossings either side
    const table = kernels(cutoff, half); const taps = 2 * half;
    for (let i = 0; i < out.length; i++) {
      const centre = i * ratio; const base = Math.floor(centre); const k = table[Math.round((centre - base) * PHASES)];
      const first = base - half + 1; let sum = 0, weight = 0;
      for (let t = 0; t < taps; t++) { const j = first + t; if (j >= 0 && j < input.length) { sum += input[j] * k[t]; weight += k[t]; } }
      out[i] = weight ? sum / weight : 0;
    }
    return out;
  }

  // 16-bit PCM mono WAV (the format the server accepts).
  function encodeWav(samples, rate = 16000) {
    const buffer = new ArrayBuffer(44 + samples.length * 2); const v = new DataView(buffer);
    const w = (o, s) => [...s].forEach((c, i) => v.setUint8(o + i, c.charCodeAt(0)));
    w(0, 'RIFF'); v.setUint32(4, 36 + samples.length * 2, true); w(8, 'WAVE'); w(12, 'fmt '); v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 1, true);
    v.setUint32(24, rate, true); v.setUint32(28, rate * 2, true); v.setUint16(32, 2, true); v.setUint16(34, 16, true); w(36, 'data'); v.setUint32(40, samples.length * 2, true);
    for (let i = 0; i < samples.length; i++) v.setInt16(44 + i * 2, Math.max(-1, Math.min(1, samples[i])) * 0x7fff, true);
    return buffer;
  }

  // Energy endpointer with an adaptive noise floor. Speech starts above the onset level and continues while the signal stays
  // above a lower hold level (hysteresis), so quiet word endings and short breaths do not end the utterance. A pre-roll keeps the
  // soft first syllable; trailing silence beyond keepTailMs is dropped (long silence invites Whisper hallucinations).
  // The floor is the quietest recent frame (minimum statistics), tracked during speech too, and nothing can start during the
  // first calibrationMs: steady fan or air-conditioning noise must never be taken for speech that never ends.
  // push() returns null, { type: 'start' } or { type: 'end', samples, rate, voicedMs }.
  function createEndpointer(options = {}) {
    const cfg = { preRollMs: 500, onsetMs: 120, endSilenceMs: 1200, keepTailMs: 350, maxSeconds: 28, minVoicedMs: 300, calibrationMs: 300, floorWindowMs: 1500, ...options };
    const s = { floor: 0.004, speaking: false, speechMs: 0, silenceMs: 0, voicedMs: 0, chunks: [], pre: [], preMs: 0, silentTail: [], heardMs: 0, recent: [] };
    const ms = (frame, rate) => (frame.length / rate) * 1000;
    function track(rms, frameMs) {
      s.heardMs += frameMs; s.recent.push(rms);
      if (s.recent.length > Math.max(4, cfg.floorWindowMs / frameMs)) s.recent.shift();
      const quietest = Math.min(...s.recent) * 1.15;
      if (s.heardMs <= cfg.calibrationMs) s.floor = Math.max(0.002, quietest);
      else if (quietest > s.floor) s.floor = Math.min(0.08, s.floor * 0.9 + quietest * 0.1);
    }
    function clearPreRoll() { s.pre = []; s.preMs = 0; s.speechMs = 0; }
    function begin(frames, rate) { s.speaking = true; s.chunks = [...frames]; s.silentTail = []; s.silenceMs = 0; s.voicedMs = frames.reduce((n, f) => n + ms(f, rate), 0); clearPreRoll(); }
    function reset() { s.speaking = false; s.chunks = []; s.silentTail = []; s.silenceMs = 0; s.voicedMs = 0; clearPreRoll(); }
    function finish(rate) {
      let kept = 0; const tail = [];
      for (const f of s.silentTail) { if (kept >= cfg.keepTailMs) break; tail.push(f); kept += ms(f, rate); }
      const frames = [...s.chunks, ...tail]; const voiced = s.voicedMs; reset();
      if (voiced < cfg.minVoicedMs) return null;
      const length = frames.reduce((n, f) => n + f.length, 0); const samples = new Float32Array(length);
      let o = 0; frames.forEach(f => { samples.set(f, o); o += f.length; });
      return { type: 'end', samples, rate, voicedMs: voiced };
    }
    function push(frame, rms, rate) {
      const frameMs = ms(frame, rate);
      track(rms, frameMs);
      const onset = Math.max(s.floor * 3.2, 0.011); const hold = Math.max(s.floor * 2, 0.007);
      if (!s.speaking) {
        s.pre.push(frame); s.preMs += frameMs;
        while (s.preMs > cfg.preRollMs && s.pre.length > 1) s.preMs -= ms(s.pre.shift(), rate);
        if (rms > onset && s.heardMs > cfg.calibrationMs) {
          s.speechMs += frameMs;
          if (s.speechMs >= cfg.onsetMs) { const frames = s.pre; begin(frames, rate); return { type: 'start' }; }
        } else { if (rms <= onset) s.floor = Math.min(0.08, s.floor * 0.97 + rms * 0.03); s.speechMs = Math.max(0, s.speechMs - frameMs); }
        return null;
      }
      if (rms > hold) { s.chunks.push(...s.silentTail, frame); s.silentTail = []; s.silenceMs = 0; s.voicedMs += frameMs; }
      else { s.silentTail.push(frame); s.silenceMs += frameMs; }
      const seconds = (s.chunks.reduce((n, f) => n + f.length, 0) + s.silentTail.reduce((n, f) => n + f.length, 0)) / rate;
      return s.silenceMs >= cfg.endSilenceMs || seconds >= cfg.maxSeconds ? finish(rate) : null;
    }
    return { push, begin, reset, clearPreRoll, get speaking() { return s.speaking; }, get floor() { return s.floor; }, config: cfg };
  }

  const api = Object.freeze({ resample, encodeWav, createEndpointer });
  if (typeof module === 'object' && module.exports) module.exports = api; else global.OrangeRadarAudio = api;
})(typeof window !== 'undefined' ? window : globalThis);
