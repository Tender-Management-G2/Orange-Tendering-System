// ORANGE RADAR - Live AI Market Analyst (text + continuous voice conversation).
// Turn loop: microphone (AudioWorklet) -> voice-activity detection -> 16 kHz WAV -> local Whisper (server) -> streamed answer from
// the local model with whitelisted page commands -> verified sentences -> local Piper speech queue -> back to listening.
// The microphone is requested only when a conversation starts and released when it ends or the page is left.
// While the analyst speaks, captured audio is ignored unless the officer enables the experimental barge-in option.
(() => {
  'use strict';
  const root = document.querySelector('[data-radar]');
  if (!root) return;
  const $ = s => root.querySelector(s);
  const csrf = root.dataset.csrf;
  const url = k => root.dataset[`url${k}`];
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const radar = () => window.OrangeRadar;
  const STATES = { idle: 'Idle', listening: 'Listening…', transcribing: 'Transcribing…', analyzing: 'Analyzing…', speaking: 'Speaking', interrupted: 'Interrupted', error: 'Error', ended: 'Ended' };

  const conv = { state: 'idle', active: false, turn: 0, controller: null, history: [], transcript: [], muted: false, speaker: true, bargein: false, language: 'auto', speed: 1, ttsAvailable: true, sttAvailable: true };
  let audioCtx = null, outCtx = null, stream = null, sourceNode = null, workletNode = null, inAnalyser = null, outAnalyser = null;
  const vad = { floor: 0.004, speaking: false, speechMs: 0, silenceMs: 0, chunks: [], pre: [], preMs: 0, bargeMs: 0 };
  let inputLevel = 0;

  // ---------- state ----------
  function setState(s) {
    conv.state = s; root.dataset.voice = s;
    $('[data-voice-state]').textContent = STATES[s] || s;
    $('[data-orb]').setAttribute('aria-label', conv.active ? `Conversation ${STATES[s]}. Activate to end the conversation.` : 'Start live conversation');
  }
  const liveButton = root.querySelector('[data-action="live"]');
  function setLive(on) {
    conv.active = on;
    liveButton.setAttribute('aria-pressed', on);
    liveButton.querySelector('[data-live-label]').textContent = on ? 'End live conversation' : 'Start live conversation';
  }

  // ---------- transcript ----------
  const list = $('[data-transcript]');
  function message(role, html, extra = '') {
    const li = document.createElement('li');
    li.className = `rd-msg rd-msg-${role} ${extra}`; li.dir = 'auto'; li.innerHTML = html;
    list.append(li); list.scrollTop = list.scrollHeight;
    return li;
  }
  const scroll = () => { list.scrollTop = list.scrollHeight; };

  // ---------- audio capture & voice activity detection ----------
  async function startConversation() {
    if (conv.active) return;
    if (!navigator.mediaDevices?.getUserMedia || !window.AudioWorkletNode) { message('error', 'This browser cannot capture audio here. Use the text box instead.'); return; }
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true, channelCount: 1 } });
    } catch (e) { setState('error'); message('error', `Microphone access was not granted (${esc(e.name)}). You can still type questions.`); return; }
    audioCtx = new AudioContext({ latencyHint: 'interactive' });
    await audioCtx.audioWorklet.addModule(root.dataset.worklet);
    sourceNode = audioCtx.createMediaStreamSource(stream);
    inAnalyser = audioCtx.createAnalyser(); inAnalyser.fftSize = 256;
    workletNode = new AudioWorkletNode(audioCtx, 'radar-capture');
    workletNode.port.onmessage = e => onFrame(e.data.frame, e.data.rms);
    sourceNode.connect(inAnalyser); sourceNode.connect(workletNode);
    // The worklet must be pulled by the graph; route it through a muted gain so nothing is played back.
    const silent = audioCtx.createGain(); silent.gain.value = 0; workletNode.connect(silent).connect(audioCtx.destination);
    Object.assign(vad, { floor: 0.004, speaking: false, speechMs: 0, silenceMs: 0, chunks: [], pre: [], preMs: 0, bargeMs: 0 });
    setLive(true); setState('listening');
    message('system', 'Live conversation started. Speak naturally; I answer when you pause. Press Esc or Stop to interrupt me.');
    fetch(url('Warm'), { method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: csrf } }).catch(() => { });
  }

  function endConversation(silent = false) {
    if (!conv.active && !stream) return;
    interrupt(true);
    stream?.getTracks().forEach(t => t.stop()); stream = null;
    try { workletNode?.disconnect(); sourceNode?.disconnect(); } catch { /* already disconnected */ }
    audioCtx?.close(); audioCtx = null; workletNode = null; sourceNode = null; inAnalyser = null; outAnalyser = null;
    setLive(false); setState('ended');
    if (!silent) message('system', 'Conversation ended. The microphone is released.');
    setTimeout(() => { if (!conv.active) setState('idle'); }, 1500);
  }

  function onFrame(frame, rms) {
    inputLevel = inputLevel * 0.7 + rms * 0.3;
    if (!conv.active || conv.muted || !audioCtx) return;
    const frameMs = (frame.length / audioCtx.sampleRate) * 1000;
    // Pre-roll keeps the first syllable that triggered detection.
    vad.pre.push(frame); vad.preMs += frameMs;
    while (vad.preMs > 360 && vad.pre.length > 1) vad.preMs -= (vad.pre.shift().length / audioCtx.sampleRate) * 1000;

    if (conv.state === 'speaking') {
      if (!conv.bargein) return; // the analyst's own voice is never treated as a command
      const loud = rms > Math.max(vad.floor * 7, 0.045);
      vad.bargeMs = loud ? vad.bargeMs + frameMs : Math.max(0, vad.bargeMs - frameMs * 2);
      if (vad.bargeMs > 450) {
        interrupt(false, 'You interrupted - listening.');
        vad.speaking = true; vad.chunks = [...vad.pre]; vad.silenceMs = 0; vad.bargeMs = 0;
        setState('listening');
      }
      return;
    }
    if (conv.state !== 'listening') return;
    const threshold = Math.max(vad.floor * 3.2, 0.011);
    if (rms > threshold) {
      vad.speechMs += frameMs; vad.silenceMs = 0;
      if (!vad.speaking && vad.speechMs >= 150) { vad.speaking = true; vad.chunks = [...vad.pre]; }
    } else {
      if (!vad.speaking) { vad.floor = Math.min(0.03, vad.floor * 0.97 + rms * 0.03); vad.speechMs = Math.max(0, vad.speechMs - frameMs); }
      else vad.silenceMs += frameMs;
    }
    if (vad.speaking) {
      vad.chunks.push(frame);
      const total = vad.chunks.reduce((n, c) => n + c.length, 0) / audioCtx.sampleRate;
      if (vad.silenceMs >= 850 || total >= 15) endUtterance();
    }
  }

  function endUtterance() {
    const chunks = vad.chunks; vad.speaking = false; vad.chunks = []; vad.speechMs = 0; vad.silenceMs = 0;
    const length = chunks.reduce((n, c) => n + c.length, 0);
    if (length / audioCtx.sampleRate < 0.5) return;
    const samples = new Float32Array(length); let o = 0; chunks.forEach(c => { samples.set(c, o); o += c.length; });
    voiceTurn(encodeWav(resample(samples, audioCtx.sampleRate, 16000)), length / audioCtx.sampleRate);
  }

  function resample(input, from, to) {
    if (from === to) return input;
    const ratio = from / to; const out = new Float32Array(Math.floor(input.length / ratio));
    for (let i = 0; i < out.length; i++) {
      const start = Math.floor(i * ratio), end = Math.min(input.length, Math.floor((i + 1) * ratio));
      let sum = 0; for (let j = start; j < end; j++) sum += input[j];
      out[i] = sum / Math.max(1, end - start); // box filter = simple anti-aliasing for speech
    }
    return out;
  }
  function encodeWav(samples) {
    const buffer = new ArrayBuffer(44 + samples.length * 2); const v = new DataView(buffer);
    const w = (o, s) => [...s].forEach((c, i) => v.setUint8(o + i, c.charCodeAt(0)));
    w(0, 'RIFF'); v.setUint32(4, 36 + samples.length * 2, true); w(8, 'WAVE'); w(12, 'fmt '); v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 1, true);
    v.setUint32(24, 16000, true); v.setUint32(28, 32000, true); v.setUint16(32, 2, true); v.setUint16(34, 16, true); w(36, 'data'); v.setUint32(40, samples.length * 2, true);
    for (let i = 0; i < samples.length; i++) v.setInt16(44 + i * 2, Math.max(-1, Math.min(1, samples[i])) * 0x7fff, true);
    return new Blob([buffer], { type: 'audio/wav' });
  }

  async function voiceTurn(wav, seconds) {
    const id = ++conv.turn; const started = performance.now();
    setState('transcribing');
    const form = new FormData(); form.append('audio', wav, 'utterance.wav'); form.append('language', conv.language);
    let result;
    try {
      const r = await fetch(url('Transcribe'), { method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: csrf }, body: form });
      result = await r.json();
      if (!r.ok) throw new Error(result.error || `Transcription failed (${r.status})`);
    } catch (e) {
      if (id !== conv.turn) return;
      message('error', `Speech recognition: ${esc(e.message)} You can type instead.`);
      setState(conv.active ? 'listening' : 'idle'); return;
    }
    if (id !== conv.turn) return;
    if (!result.text) { setState('listening'); $('[data-voice-metrics]').textContent = 'No speech recognised - still listening.'; return; }
    await ask(result.text, { spoken: true, language: result.language, sttMs: result.processingMs, audioSeconds: seconds, turnStart: started });
  }

  // ---------- asking the analyst (streamed) ----------
  async function ask(text, options = {}) {
    text = (text || '').trim(); if (!text) return;
    if (conv.controller || ttsQueue.length || currentSource) interrupt(false);
    const id = ++conv.turn; const controller = new AbortController(); conv.controller = controller;
    const started = options.turnStart ?? performance.now();
    message('user', esc(text));
    const bubble = message('assistant', '', 'is-streaming');
    const tools = []; let raw = ''; const sentences = []; let firstAudio = null; let firstToken = null;
    setState('analyzing');
    const language = conv.language !== 'auto' ? conv.language : options.language && options.language !== 'auto' ? options.language : null;
    const speak = conv.speaker && (conv.active || options.speakAnyway);
    try {
      const response = await fetch(url('Ask'), {
        method: 'POST', credentials: 'same-origin', signal: controller.signal,
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf },
        body: JSON.stringify({ message: text, context: radar()?.context(), history: conv.history.slice(-6), language })
      });
      if (!response.ok || !response.body) throw new Error(`The analyst is unavailable (${response.status}).`);
      const reader = response.body.getReader(); const decoder = new TextDecoder(); let buffer = '';
      for (; ;) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        let nl;
        while ((nl = buffer.indexOf('\n')) >= 0) {
          const line = buffer.slice(0, nl).trim(); buffer = buffer.slice(nl + 1);
          if (!line || id !== conv.turn) continue;
          const ev = JSON.parse(line);
          if (ev.type === 'delta') { raw += ev.text; firstToken ??= performance.now(); render(); }
          else if (ev.type === 'sentence') {
            sentences.push(ev); render();
            if (speak) enqueueSpeech(ev.verified ? ev.speak : (language === 'ar' || /[؀-ۿ]/.test(ev.text) ? 'يحتوي هذا الجزء على رقم لم يمكن التحقق منه، لذلك لم أقرأه.' : 'This part contains a figure I could not verify, so I did not read it aloud.'),
              /[؀-ۿ]/.test(ev.text) ? 'ar' : 'en', id).then(t => { if (t && firstAudio == null) firstAudio = t; });
          }
          else if (ev.type === 'tool') { tools.push(ev); render(); }
          else if (ev.type === 'command') radar()?.apply(ev.command);
          else if (ev.type === 'error') { bubble.classList.remove('is-streaming'); bubble.className = 'rd-msg rd-msg-error'; bubble.textContent = ev.message; }
          else if (ev.type === 'done') {
            const verified = sentences.every(s => s.verified);
            conv.history.push({ role: 'user', text }, { role: 'assistant', text: ev.text });
            conv.transcript.push({ role: 'user', text, verified: null }, { role: 'assistant', text: ev.text, verified });
            const m = ev.metrics || {};
            metrics({ model: ev.model, firstTokenMs: m.firstTokenMs, totalMs: m.totalMs, tps: m.tokensPerSecond, prompt: m.promptTokens, stt: options.sttMs, audio: options.audioSeconds, tools: ev.toolCalls, started });
          }
        }
      }
    } catch (e) {
      if (e.name === 'AbortError') return;
      bubble.className = 'rd-msg rd-msg-error'; bubble.textContent = e.message;
      setState(conv.active ? 'listening' : 'error');
      return;
    } finally { if (conv.controller === controller) conv.controller = null; bubble.classList.remove('is-streaming'); }
    if (id !== conv.turn) return;
    await drainSpeech();
    if (id !== conv.turn) return;
    if (firstAudio != null) $('[data-voice-metrics]').textContent += ` · first audio ${Math.round(firstAudio - started)} ms`;
    setState(conv.active ? 'listening' : 'idle');

    function render() {
      // Verified sentences are plain; sentences with an unverifiable figure are marked and explained.
      const done = sentences.map(s => s.verified ? esc(s.text) : `<span class="rd-unverified" title="Contains a figure not found in the verified data: ${esc(s.unverified.join(', '))}">${esc(s.text)}</span>`).join(' ');
      const consumed = sentences.reduce((n, s) => n + s.text.length, 0);
      const rest = raw.length > consumed ? esc(raw.slice(sentences.length ? raw.lastIndexOf(sentences[sentences.length - 1].text) + sentences[sentences.length - 1].text.length : 0)) : '';
      const flagged = sentences.filter(s => !s.verified);
      bubble.innerHTML = `${done} ${rest}` + (tools.length ? `<div class="rd-tools">${tools.map(t => `<span>${t.ok ? '✓' : '✗'} ${esc(t.name.replace(/_/g, ' '))}</span>`).join('')}</div>` : '') +
        (flagged.length ? `<span class="rd-flag">⚑ ${flagged.length} sentence(s) contain a figure that could not be verified against Orange Radar data and were not read aloud.</span>` : '');
      scroll();
    }
  }

  function metrics(m) {
    const parts = [m.model, m.audio ? `heard ${m.audio.toFixed(1)} s` : null, m.stt ? `STT ${Math.round(m.stt)} ms` : null, m.firstTokenMs ? `first token ${Math.round(m.firstTokenMs)} ms` : null,
      m.tps ? `${m.tps} tok/s` : null, m.prompt ? `${m.prompt} prompt tokens` : null, m.tools ? `${m.tools} tool call(s)` : null, `turn ${Math.round(performance.now() - m.started)} ms`].filter(Boolean);
    $('[data-voice-metrics]').textContent = parts.join(' · ');
  }

  // ---------- speech output (Piper) ----------
  const ttsQueue = []; let currentSource = null; let drainWaiters = [];
  function outputContext() {
    if (audioCtx) return audioCtx;
    outCtx ??= new AudioContext();
    return outCtx;
  }
  function enqueueSpeech(text, language, turn) {
    if (!conv.ttsAvailable || !text) return Promise.resolve(null);
    const ctx = outputContext();
    const item = { turn, started: null, resolveStart: null };
    const startedPromise = new Promise(r => (item.resolveStart = r));
    item.audio = fetch(url('Speak'), { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf }, body: JSON.stringify({ text, language, speed: conv.speed }) })
      .then(async r => { if (!r.ok) { const e = await r.json().catch(() => ({})); throw new Error(e.error || `speech ${r.status}`); } return ctx.decodeAudioData(await r.arrayBuffer()); });
    ttsQueue.push(item);
    if (!currentSource) playNext();
    return startedPromise;
  }
  async function playNext() {
    const item = ttsQueue.shift();
    if (!item) { currentSource = null; drainWaiters.forEach(r => r()); drainWaiters = []; return; }
    currentSource = { pending: true };
    let buffer;
    try { buffer = await item.audio; }
    catch (e) {
      conv.ttsAvailable = !/not configured|not found/i.test(e.message);
      message('system', `Voice output unavailable: ${esc(e.message)}`);
      item.resolveStart(null); currentSource = null; ttsQueue.length = 0; drainWaiters.forEach(r => r()); drainWaiters = []; return;
    }
    if (item.turn !== conv.turn && item.turn !== 'narration') { item.resolveStart(null); playNext(); return; }
    const ctx = outputContext();
    if (ctx.state === 'suspended') await ctx.resume().catch(() => { });
    outAnalyser = ctx.createAnalyser(); outAnalyser.fftSize = 256;
    const src = ctx.createBufferSource(); src.buffer = buffer; src.connect(outAnalyser).connect(ctx.destination);
    currentSource = src;
    src.onended = () => { if (currentSource === src) playNext(); };
    if (conv.state !== 'speaking' && item.turn !== 'narration') setState('speaking');
    src.start(); item.resolveStart(performance.now());
  }
  function drainSpeech() { return !currentSource && !ttsQueue.length ? Promise.resolve() : new Promise(r => drainWaiters.push(r)); }
  function stopSpeaking() {
    ttsQueue.length = 0;
    if (currentSource?.stop) { currentSource.onended = null; try { currentSource.stop(); } catch { /* already stopped */ } }
    currentSource = null; drainWaiters.forEach(r => r()); drainWaiters = [];
  }
  // Cancels the outdated generation, its speech and its pending visual actions.
  function interrupt(silent, note) {
    const busy = conv.controller || currentSource || ttsQueue.length;
    conv.turn++;
    conv.controller?.abort(); conv.controller = null;
    stopSpeaking();
    radar()?.cancelAnimations();
    list.querySelectorAll('.is-streaming').forEach(b => { b.classList.remove('is-streaming'); b.insertAdjacentHTML('beforeend', ' <em class="rd-muted">(interrupted)</em>'); });
    if (busy && !silent) { setState('interrupted'); if (note) $('[data-voice-metrics]').textContent = note; setTimeout(() => { if (conv.state === 'interrupted') setState(conv.active ? 'listening' : 'idle'); }, 600); }
  }

  // Briefing narration: resolves when the scene's sentences have been spoken (or immediately if speech is off).
  function narrate(text, language) {
    if (!conv.speaker || !conv.ttsAvailable) return null;
    stopSpeaking();
    const parts = text.match(/[^.!?؟]+[.!?؟]?/g)?.map(s => s.trim()).filter(Boolean) || [text];
    parts.forEach(p => enqueueSpeech(p, language, 'narration'));
    return drainSpeech();
  }

  // ---------- orb & waveform (real audio levels) ----------
  const orbCanvas = $('[data-orb-canvas]'); const orb = orbCanvas.getContext('2d');
  const waveCanvas = $('[data-wave]'); const wave = waveCanvas.getContext('2d');
  const levels = new Array(90).fill(0);
  const freq = new Uint8Array(128);
  function draw(now) {
    const analyser = conv.state === 'speaking' ? outAnalyser : conv.active ? inAnalyser : outAnalyser && currentSource ? outAnalyser : null;
    let level = 0;
    if (analyser) { analyser.getByteFrequencyData(freq); level = freq.slice(2, 60).reduce((a, b) => a + b, 0) / (58 * 255); }
    levels.push(level); levels.shift();
    const css = n => getComputedStyle(root).getPropertyValue(n).trim();
    const color = conv.state === 'listening' ? css('--rd-good') : css('--rd-orange');
    // Orb halo
    orb.clearRect(0, 0, 240, 240);
    const bars = 64; const breathing = reduced.matches ? 0 : (Math.sin(now / 700) + 1) * 0.03;
    for (let i = 0; i < bars; i++) {
      const a = (i / bars) * Math.PI * 2; const v = analyser ? freq[2 + (i % 40)] / 255 : breathing;
      const r1 = 64, r2 = 64 + 6 + v * 46;
      orb.strokeStyle = color; orb.globalAlpha = 0.25 + v * 0.75; orb.lineWidth = 3; orb.lineCap = 'round';
      orb.beginPath(); orb.moveTo(120 + Math.cos(a) * r1, 120 + Math.sin(a) * r1); orb.lineTo(120 + Math.cos(a) * r2, 120 + Math.sin(a) * r2); orb.stroke();
    }
    orb.globalAlpha = 1;
    // Waveform history
    const w = waveCanvas.width, h = waveCanvas.height; wave.clearRect(0, 0, w, h);
    const step = w / levels.length;
    levels.forEach((v, i) => {
      const center = Math.abs(i - levels.length / 2) < 16; if (center) return; // keep the orb area clear
      const bh = Math.max(2, v * h * 1.6);
      wave.fillStyle = color; wave.globalAlpha = 0.25 + v * 0.75;
      wave.fillRect(i * step + 1, (h - bh) / 2, Math.max(1.5, step - 2.5), bh);
    });
    wave.globalAlpha = 1;
    requestAnimationFrame(draw);
  }
  requestAnimationFrame(draw);

  // ---------- suggestions ----------
  function suggestions() {
    const sel = radar()?.selected || [];
    const n = id => radar()?.name(id) || '';
    const items = sel.length >= 2 ? [`Compare ${n(sel[0])} and ${n(sel[1])}.`, 'Are their documents similar?', `Show the relationship path between ${n(sel[0])} and ${n(sel[1])}.`, `What happens if ${n(sel[0])} is unavailable?`]
      : sel.length === 1 ? [`Why is ${n(sel[0])} connected to other suppliers?`, `Show ${n(sel[0])}'s historical prices.`, `What happens if ${n(sel[0])} is unavailable?`, 'Explain this data point.']
        : ['Analyze our supplier market.', 'Show me the most active suppliers.', 'Show me recurring supplier relationships.', 'Walk me through the most important findings.', 'حلل سوق الموردين لدينا'];
    items.push('Generate a report about what we discussed.');
    $('[data-suggestions]').innerHTML = items.map(t => `<button type="button" data-suggest="${esc(t)}" dir="auto">${esc(t)}</button>`).join('');
  }
  window.addEventListener('radar:selection', suggestions);
  window.addEventListener('radar:data', suggestions);

  // ---------- controls ----------
  $('[data-ask-form]').addEventListener('submit', e => { e.preventDefault(); const input = $('[data-ask-input]'); const t = input.value; input.value = ''; ask(t); });
  root.addEventListener('click', e => {
    const b = e.target.closest('button'); if (!b) return;
    if (b.dataset.suggest) ask(b.dataset.suggest);
    else if (b.dataset.action === 'live' || b.hasAttribute('data-orb')) { conv.active ? endConversation() : startConversation(); }
    else if (b.dataset.voice === 'stop') { interrupt(false, 'Stopped speaking.'); }
    else if (b.dataset.voice === 'end') endConversation();
    else if (b.dataset.voice === 'mute') { conv.muted = !conv.muted; b.setAttribute('aria-pressed', conv.muted); b.setAttribute('aria-label', conv.muted ? 'Unmute microphone' : 'Mute microphone'); stream?.getAudioTracks().forEach(t => (t.enabled = !conv.muted)); }
    else if (b.dataset.voice === 'speaker') { conv.speaker = !conv.speaker; b.setAttribute('aria-pressed', conv.speaker); b.setAttribute('aria-label', conv.speaker ? 'Voice replies on' : 'Voice replies off'); if (!conv.speaker) stopSpeaking(); }
  });
  $('[data-rd-voice-language]').addEventListener('change', e => (conv.language = e.target.value));
  $('[data-voice-speed]').addEventListener('input', e => (conv.speed = +e.target.value));
  $('[data-voice-bargein]').addEventListener('change', e => (conv.bargein = e.target.checked));
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape' && (currentSource || conv.controller)) { interrupt(false, 'Interrupted.'); if (radar()?.briefingActive) radar().pauseBriefing(); }
  });
  window.addEventListener('pagehide', () => endConversation(true));

  // Component status (local model, Whisper, Piper) - shown honestly, nothing is assumed.
  fetch(url('Status'), { credentials: 'same-origin' }).then(r => r.json()).then(s => {
    const piper = s.piper || {};
    conv.ttsAvailable = Object.values(piper).every(v => v !== 'not found' && v !== 'script missing');
    conv.sttAvailable = !/not found/.test(s.whisper);
    $('[data-voice-model]').textContent = `${s.chatModel}${s.chatModelInstalled ? '' : ' (not installed)'} · Ollama ${s.ollama} · Whisper ${s.whisper} · Piper ${conv.ttsAvailable ? 'ready' : 'unavailable'}`;
  }).catch(() => { $('[data-voice-model]').textContent = 'Status unavailable'; });

  window.OrangeRadarVoice = Object.freeze({
    ask: t => { radar()?.openPanel('analyst'); ask(t); },
    prefill: t => { radar()?.openPanel('analyst'); const i = $('[data-ask-input]'); i.value = t; i.focus(); },
    narrate, stopSpeaking, transcript: () => conv.transcript.slice(-30),
    language: () => conv.language
  });
  setState('idle');
})();
