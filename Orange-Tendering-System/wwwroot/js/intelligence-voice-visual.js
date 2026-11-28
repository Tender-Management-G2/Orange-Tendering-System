// ORANGE INTELLIGENCE - AI Studio: the animated voice orb of the Live Voice call (Views/Intelligence/Index.cshtml).
//
// STUDY NOTE - this file is presentation only. The call itself (token, Gemini Live, microphone, playback) is in
// intelligence-audio.js and is not changed by this file. Understand it at the architecture level:
//   * intelligence-audio.js sets the panel's data-state (idle | connecting | listening | thinking | speaking) and, on a failure,
//     data-outcome="error". It also hands this file its two Web Audio analysers: window.OrangeVoiceVisual.connect(mic, speaker).
//   * Every frame this file reads the state and the REAL analyser of that state (microphone while listening/thinking,
//     Gemini's voice while speaking) and draws a canvas. With no live signal the orb only breathes slowly - it never imitates audio.
// History: the particle sphere, the three spectrum ribbons, the halo, the thinking orbit and the mirrored spectrum come from the
// old orange-radar-voice.js (last version in c0514c3, removed in daf0025). New here: the liquid core, voice ripples when the
// user speaks, speech sparks when Gemini speaks, the error colour and smooth colour transitions between states.
(function () {
  'use strict';

  const panel = document.querySelector('[data-voice-panel]');
  const canvas = panel?.querySelector('[data-voice-canvas]');
  window.OrangeVoiceVisual = { connect() { } };
  if (!panel || !canvas) return;

  const ctx = canvas.getContext('2d');
  const activity = panel.querySelector('[data-voice-activity]');
  const host = document.querySelector('[data-radar]') || document.documentElement;
  const arabic = host.dataset?.uiLanguage === 'ar';
  const T = (en, ar) => (arabic ? ar : en);
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');

  const analysers = { mic: null, speaker: null };
  window.OrangeVoiceVisual = {
    // Called by intelligence-audio.js when the call starts (both analysers) and ends (null, null).
    connect(mic, speaker) { analysers.mic = mic || null; analysers.speaker = speaker || null; wake(); }
  };

  const BANDS = 48;
  const bands = new Float32Array(BANDS);
  let samples = new Float32Array(1024), freq = new Uint8Array(512);
  let level = 0, clock = 0, lastT = performance.now(), raf = 0, visible = true, shake = 0;
  let tint = null, userSpeaking = false, lastRipple = 0;
  const ripples = [], sparks = [];
  const SPEECH_VOLUME = 0.02; // the same threshold intelligence-audio.js uses to decide that the user is speaking

  const cssVar = name => getComputedStyle(host).getPropertyValue(name).trim();
  const hexRgb = hex => { const h = String(hex || '#ff7900').replace('#', ''); const n = parseInt(h.length === 3 ? h.split('').map(c => c + c).join('') : h.slice(0, 6), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; };
  const rgba = (c, a) => `rgba(${c.map(Math.round).join(',')},${Math.max(0, Math.min(1, a)).toFixed(3)})`;
  const isDark = () => document.documentElement.dataset.theme !== 'light';
  const state = () => panel.dataset.state || 'idle';
  const failed = () => panel.dataset.outcome === 'error' && state() === 'idle';

  // Fibonacci points of the particle shell.
  const SHELL = (() => { const n = 680, pts = []; for (let i = 0; i < n; i++) { const y = 1 - (i / (n - 1)) * 2, r = Math.sqrt(1 - y * y), a = i * 2.39996; pts.push([Math.cos(a) * r, y, Math.sin(a) * r]); } return pts; })();

  function signal() {
    const s = state();
    if (s === 'speaking') return analysers.speaker;
    if (s === 'listening' || s === 'thinking') return analysers.mic;
    return null;
  }
  function stateColor() {
    const s = state();
    if (failed()) return hexRgb('#e0483e');
    if (s === 'speaking') return hexRgb(cssVar('--rd-orange-strong'));
    if (s === 'thinking') return hexRgb(cssVar('--rd-think'));
    if (s === 'connecting') return hexRgb('#ff9a3c');
    if (s === 'listening') return hexRgb(cssVar('--rd-listen'));
    const o = hexRgb(cssVar('--rd-orange-strong')), l = hexRgb(cssVar('--rd-listen'));
    return o.map((v, i) => v * 0.82 + l[i] * 0.18);
  }

  function sizeCanvas() {
    const r = canvas.getBoundingClientRect(); const ratio = Math.min(2, window.devicePixelRatio || 1);
    const w = Math.max(1, Math.round(r.width * ratio)), h = Math.max(1, Math.round(r.height * ratio));
    if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
    return { w, h, ratio, cssW: r.width };
  }

  function readAudio(analyser) {
    if (!analyser) { for (let b = 0; b < BANDS; b++) bands[b] *= 0.86; return 0; }
    if (samples.length !== analyser.fftSize) samples = new Float32Array(analyser.fftSize);
    if (freq.length !== analyser.frequencyBinCount) freq = new Uint8Array(analyser.frequencyBinCount);
    analyser.getFloatTimeDomainData(samples); analyser.getByteFrequencyData(freq);
    let sum = 0; for (let i = 0; i < samples.length; i++) sum += samples[i] * samples[i];
    const rms = Math.sqrt(sum / samples.length);
    // Log-spaced bands across the speech range (the first ~40% of the bins).
    const top = Math.max(BANDS, Math.floor(freq.length * 0.4));
    for (let b = 0; b < BANDS; b++) {
      const i0 = Math.floor(Math.pow(top, b / BANDS)), i1 = Math.max(i0 + 1, Math.floor(Math.pow(top, (b + 1) / BANDS)));
      let m = 0; for (let i = i0; i < i1 && i < freq.length; i++) m = Math.max(m, freq[i]);
      const v = m / 255; bands[b] += (v - bands[b]) * (v > bands[b] ? 0.5 : 0.15);
    }
    return rms;
  }

  function paint(c, cx, cy, R, ratio, col, t, audio, amp, dark) {
    const teal = hexRgb(cssVar('--rd-listen')), orange = hexRgb(cssVar('--rd-orange-strong'));
    const s = state();

    // 1. Halo.
    // The halo fades out before the canvas edge, so no rectangle is ever visible.
    const haloR = Math.min(R * (2.15 + level * 0.7), cx, cy, c.canvas.width - cx, c.canvas.height - cy);
    const halo = c.createRadialGradient(cx, cy, R * 0.2, cx, cy, haloR);
    halo.addColorStop(0, rgba(col, (dark ? 0.3 : 0.18) + level * 0.28)); halo.addColorStop(0.45, rgba(col, dark ? 0.08 : 0.05)); halo.addColorStop(1, rgba(col, 0));
    c.fillStyle = halo; c.beginPath(); c.arc(cx, cy, haloR, 0, Math.PI * 2); c.fill();

    // 2. Voice ripples (only spawned by a real microphone signal above the speech threshold).
    for (let i = ripples.length - 1; i >= 0; i--) {
      const r = ripples[i]; const age = (t - r.t0) / 1.4; if (age >= 1) { ripples.splice(i, 1); continue; }
      c.beginPath(); c.arc(cx, cy, R * (1.05 + age * 1.15), 0, Math.PI * 2);
      c.strokeStyle = rgba(col, (1 - age) * 0.55 * r.power); c.lineWidth = (2.4 - age * 1.6) * ratio; c.stroke();
    }

    c.save(); c.globalCompositeOperation = dark ? 'lighter' : 'source-over';

    // 3. Liquid core: a soft closed shape whose outline flows slowly and bulges with the live spectrum.
    const coreR = R * 0.64;
    c.beginPath();
    for (let i = 0; i <= 96; i++) {
      const a = (i / 96) * Math.PI * 2; const bi = Math.min(BANDS - 1, Math.floor(Math.abs(Math.sin(a)) * BANDS));
      const flow = Math.sin(a * 3 + t * 0.9) * 0.045 + Math.sin(a * 5 - t * 1.3) * 0.03 + Math.sin(a * 2 + t * 0.4) * 0.02;
      const r = coreR * (1 + flow * (0.6 + amp * 6) + bands[bi] * audio * 0.42);
      const x = cx + Math.cos(a) * r, y = cy + Math.sin(a) * r;
      if (i) c.lineTo(x, y); else c.moveTo(x, y);
    }
    c.closePath();
    const core = c.createRadialGradient(cx - R * 0.22, cy - R * 0.26, R * 0.05, cx, cy, coreR * 1.2);
    core.addColorStop(0, rgba(col.map(v => v + (255 - v) * 0.7), dark ? 0.72 : 0.66));
    core.addColorStop(0.45, rgba(col, dark ? 0.42 : 0.36));
    core.addColorStop(1, rgba(col, 0.02));
    c.fillStyle = core; c.fill();

    // 4. Ribbons: three tilted, breathing rings; their bulge is the live spectrum.
    for (let k = 0; k < 3; k++) {
      const tilt = -0.5 + k * 0.55 + Math.sin(t * 0.25 + k) * 0.08; const ct = Math.cos(tilt), st = Math.sin(tilt);
      c.beginPath();
      for (let i = 0; i <= 120; i++) {
        const a = (i / 120) * Math.PI * 2; const bi = Math.min(BANDS - 1, Math.floor(Math.abs(Math.sin(a / 2)) * BANDS));
        const r = R * (1.06 + k * 0.05 + Math.sin(a * 3 + t * (0.7 + k * 0.25)) * amp * 1.6 + bands[bi] * audio * 1.1);
        const ex = Math.cos(a) * r, ey = Math.sin(a) * r * (0.3 + k * 0.08);
        const x = cx + ex * ct - ey * st, y = cy + ex * st + ey * ct;
        if (i) c.lineTo(x, y); else c.moveTo(x, y);
      }
      const g = c.createLinearGradient(cx - R, cy - R, cx + R, cy + R);
      g.addColorStop(0, rgba(teal, 0.55 - k * 0.12)); g.addColorStop(1, rgba(orange, 0.65 - k * 0.12));
      c.strokeStyle = g; c.lineWidth = (1.7 - k * 0.3) * ratio; c.stroke();
    }

    // 5. Particle shell: points on a slowly turning sphere, pushed outward only by the real spectrum.
    const rotY = t * 0.32, rotX = 0.38 + Math.sin(t * 0.18) * 0.1;
    const cY = Math.cos(rotY), sY = Math.sin(rotY), cX = Math.cos(rotX), sX = Math.sin(rotX);
    for (let k = 0; k < SHELL.length; k++) {
      const [x0, y0, z0] = SHELL[k];
      const bi = Math.min(BANDS - 1, Math.floor((Math.acos(y0) / Math.PI) * BANDS));
      const wob = Math.sin(3 * Math.atan2(z0, x0) + t * 1.1 + y0 * 4) * amp;
      const rr = R * (1 + wob + bands[bi] * audio * 1.35);
      const x1 = x0 * cY - z0 * sY, z1 = x0 * sY + z0 * cY; const y1 = y0 * cX - z1 * sX, z2 = y0 * sX + z1 * cX;
      const depth = (z2 + 1) / 2; const mix = Math.min(1, Math.max(0, (x1 - y1 + 1.2) / 2.4));
      const pt = teal.map((v, i) => (v * (1 - mix) + orange[i] * mix) * 0.55 + col[i] * 0.45);
      c.fillStyle = rgba(pt, (0.12 + depth * 0.8) * (dark ? 1 : 0.9));
      const size = (0.5 + depth * 1.3 + bands[bi] * audio * 2.6) * ratio;
      c.beginPath(); c.arc(cx + x1 * rr, cy + y1 * rr, size, 0, Math.PI * 2); c.fill();
    }

    // 6. Speech sparks: emitted only while Gemini's real voice is playing, in proportion to its loudness.
    for (let i = sparks.length - 1; i >= 0; i--) {
      const p = sparks[i]; const age = (t - p.t0) / p.life; if (age >= 1) { sparks.splice(i, 1); continue; }
      const d = R * (1.1 + age * p.reach);
      c.fillStyle = rgba(col.map(v => v + (255 - v) * 0.4), (1 - age) * 0.9);
      c.beginPath(); c.arc(cx + Math.cos(p.a) * d, cy + Math.sin(p.a) * d, (1.8 - age) * ratio * p.size, 0, Math.PI * 2); c.fill();
    }
    c.restore();

    // 7. Orbit arcs while connecting or while Gemini prepares its answer (work in progress, not audio).
    if ((s === 'connecting' || s === 'thinking') && !reduced.matches) {
      for (let k = 0; k < 3; k++) {
        const a = t * (1.6 - k * 0.3) * (s === 'thinking' ? 1.3 : 1) + (k * Math.PI * 2) / 3; const rr = R * (1.38 + k * 0.1);
        c.beginPath(); c.arc(cx, cy, rr, a, a + 0.9); c.lineWidth = (3 - k * 0.6) * ratio; c.lineCap = 'round'; c.strokeStyle = rgba(col, 0.75 - k * 0.18); c.stroke();
        c.beginPath(); c.arc(cx + Math.cos(a + 0.9) * rr, cy + Math.sin(a + 0.9) * rr, (4 - k) * ratio, 0, Math.PI * 2); c.fillStyle = rgba(col, 0.95); c.fill();
      }
    }
  }

  // Mirrored spectrum under the orb: real bands, a calm hairline when there is no signal.
  function paintSpectrum(c, cx, baseY, width, ratio, col) {
    const x0 = cx - width / 2; const step = width / (BANDS * 2);
    for (let i = 0; i < BANDS * 2; i++) {
      const v = bands[i < BANDS ? BANDS - 1 - i : i - BANDS];
      const h = Math.max(1.5 * ratio, v * 46 * ratio);
      c.fillStyle = rgba(col, 0.22 + v * 0.7);
      const x = x0 + i * step + step * 0.22, w = Math.max(1, step * 0.56);
      c.beginPath(); c.roundRect ? c.roundRect(x, baseY - h / 2, w, h, w / 2) : c.rect(x, baseY - h / 2, w, h); c.fill();
    }
  }

  function draw(now = performance.now()) {
    raf = 0;
    if (!visible || document.hidden || panel.hidden || canvas.offsetParent === null) return;
    const dt = Math.min(0.064, (now - lastT) / 1000); lastT = now;
    const still = reduced.matches;
    if (!still) clock += dt;
    const analyser = signal();
    const rms = readAudio(analyser);
    const target = analyser ? Math.min(1, rms * 4.2) : 0;
    level += (target - level) * 0.25; if (!analyser && level < 0.002) level = 0;

    // The user is speaking: a real microphone signal above the threshold while Gemini is listening.
    const s = state();
    const nowSpeaking = s === 'listening' && rms > SPEECH_VOLUME;
    if (nowSpeaking !== userSpeaking) {
      userSpeaking = nowSpeaking;
      panel.classList.toggle('is-user-speaking', userSpeaking);
      if (activity) activity.textContent = userSpeaking ? T('Hearing you…', 'أسمعك…') : '';
    }
    if (!still && userSpeaking && clock - lastRipple > 0.42) { ripples.push({ t0: clock, power: Math.min(1, 0.4 + level) }); lastRipple = clock; }
    if (!still && s === 'speaking' && level > 0.08 && sparks.length < 70) {
      const count = Math.round(level * 3);
      for (let i = 0; i < count; i++) sparks.push({ t0: clock, a: Math.random() * Math.PI * 2, life: 0.9 + Math.random() * 0.8, reach: 0.6 + Math.random() * 0.9, size: 0.6 + Math.random() * 0.9 });
    }
    if (failed() && shake === 0 && !still) shake = 1;
    if (!failed()) shake = 0;

    const goal = stateColor();
    tint = tint ? tint.map((v, i) => v + (goal[i] - v) * 0.08) : goal;
    const colorMoving = tint.some((v, i) => Math.abs(v - goal[i]) > 0.6);
    const audio = still ? 0 : 0.18 + level * 0.3;
    const amp = still ? 0 : analyser ? 0.03 + level * 0.06 : s === 'thinking' || s === 'connecting' ? 0.05 : 0.026;

    const sz = sizeCanvas(); const dark = isDark();
    ctx.clearRect(0, 0, sz.w, sz.h);
    const R = Math.min(sz.w * 0.2, sz.h * 0.25) * (1 + (still ? 0 : level * 0.08));
    let cx = sz.w / 2; const cy = sz.h * 0.43;
    if (shake > 0.01) { cx += Math.sin(clock * 48) * shake * 7 * sz.ratio; shake *= 0.9; }
    paint(ctx, cx, cy, R, sz.ratio, tint, clock, analyser ? audio : 0, amp, dark);
    paintSpectrum(ctx, sz.w / 2, sz.h * 0.88, Math.min(sz.w * 0.8, R * 3.6), sz.ratio, tint);
    panel.style.setProperty('--voice-tint', rgba(tint, 1));

    const active = s !== 'idle' || level > 0.002 || ripples.length || sparks.length || colorMoving || shake > 0.01;
    if (!still || active) raf = requestAnimationFrame(draw);
  }
  function wake() { if (!raf) raf = requestAnimationFrame(draw); }

  // Draw only while the voice panel is on screen; the state attribute changing wakes the loop.
  new IntersectionObserver(entries => { visible = entries[entries.length - 1].isIntersecting; if (visible) wake(); }).observe(canvas);
  new MutationObserver(wake).observe(panel, { attributes: true, attributeFilter: ['data-state', 'data-outcome', 'hidden'] });
  new ResizeObserver(wake).observe(canvas);
  document.addEventListener('visibilitychange', wake);
  window.addEventListener('orange:themechange', () => { tint = null; wake(); });
  reduced.addEventListener?.('change', wake);
  wake();
})();
