// ORANGE RADAR - Live AI Market Analyst (text + continuous voice conversation).
// Turn loop: microphone (AudioWorklet) -> endpointer (orange-radar-audio.js) -> 16 kHz WAV -> local Whisper (server) -> the
// transcript is shown for checking (clear ones are sent after a short, cancellable countdown; unclear ones wait for the user) ->
// streamed answer from the local model with whitelisted page commands -> verified sentences -> Piper speech -> back to listening.
// Speech that resumes before a turn is sent is joined to the same request instead of being lost.
// One session drives two surfaces: the voice stage (docked under the Supplier Universe or in AI Studio) and the compact companion
// on the other tabs. Generated charts/tables arrive as validated "visual" events and are drawn here with allowlisted components;
// uploaded files are extracted on the server and sent with the question as untrusted, attributed data.
// The microphone is requested only when a conversation starts and released when it ends or the page is left.
// Visuals use real analyser samples only (microphone while listening, Piper playback while speaking); idle draws nothing that moves.
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
  const md = html => html.replace(/\*\*(.+?)\*\*/g, '<b>$1</b>'); // the local model sometimes adds bold markers
  const STATES = { idle: 'Idle', listening: 'Listening', transcribing: 'Transcribing', review: 'Check what I heard', analyzing: 'Analyzing', speaking: 'Speaking', interrupted: 'Interrupted', error: 'Error', ended: 'Ended', muted: 'Muted' };
  const audio = window.OrangeRadarAudio;

  const conv = { state: 'idle', active: false, turn: 0, controller: null, history: [], transcript: [], muted: false, speaker: true, bargein: false, language: 'auto', review: 'auto', speed: 1, ttsAvailable: true, sttAvailable: true, deafUntil: 0, pending: null, lastReport: null };
  let audioCtx = null, outCtx = null, stream = null, sourceNode = null, workletNode = null, inAnalyser = null;
  const outAnalysers = new WeakMap(); // one playback analyser per AudioContext, connected to the destination once
  let ep = null; // endpointer of the running conversation
  const barge = { ms: 0, recent: [], recentMs: 0 };
  // The utterance being transcribed or checked; `continuation` holds it when the user resumes speaking before it was sent.
  let pending = null; let continuation = null;

  // ---------- state (shared by the scene and the dock) ----------
  const dock = root.querySelector('[data-dock]');
  function setState(s) {
    conv.state = s; root.dataset.voice = s;
    const label = conv.muted && conv.active && s === 'listening' ? STATES.muted : STATES[s] || s;
    $('[data-voice-state]').textContent = label;
    dock.querySelector('[data-dock-state]').textContent = conv.active || s !== 'idle' ? label : 'Analyst';
    $('[data-orb]').setAttribute('aria-label', conv.active ? `Conversation ${label}. Activate to end the conversation.` : 'Start live conversation');
    root.querySelectorAll('[data-vs]').forEach(li => li.classList.toggle('is-on', li.dataset.vs === s && (s !== 'listening' || !conv.muted)));
    updateDock(); startDrawing();
  }
  const liveButton = root.querySelector('[data-action="live"]');
  function setLive(on) {
    conv.active = on;
    liveButton.setAttribute('aria-pressed', on);
    liveButton.querySelector('[data-live-label]').textContent = on ? 'End live conversation' : 'Start live conversation';
    dock.querySelector('[data-dock="live"]').setAttribute('aria-label', on ? 'End live conversation' : 'Start live conversation');
    dock.classList.toggle('is-live', on);
    updateDock();
  }
  const dockLast = text => { dock.querySelector('[data-dock-last]').textContent = text; };

  // The dock is shown while the analyst scene is off screen and the officer has started or used the analyst.
  let analystVisible = false; let used = false;
  const stageEl = root.querySelector('[data-voice-stage]');
  new IntersectionObserver(entries => { analystVisible = entries[entries.length - 1].isIntersecting; updateDock(); startDrawing(); }, { threshold: 0.2 }).observe(stageEl);
  function updateDock() {
    // The companion stands in for the voice stage on tabs that do not show it (Overview, Tenders, Forensics).
    const show = !analystVisible;
    dock.dataset.dockHidden = show ? 'false' : 'true';
    dock.inert = !show;
  }

  // ---------- transcript ----------
  const list = $('[data-transcript]');
  const jump = $('[data-jump-latest]');
  let stick = true; // follow new messages only while the officer is at the bottom of the transcript
  list.addEventListener('scroll', () => { stick = list.scrollHeight - list.scrollTop - list.clientHeight < 40; if (stick) jump.hidden = true; }, { passive: true });
  jump.addEventListener('click', () => { stick = true; list.scrollTop = list.scrollHeight; jump.hidden = true; });
  function message(role, html, extra = '') {
    const li = document.createElement('li');
    li.className = `rd-msg rd-msg-${role} ${extra}`; li.dir = 'auto'; li.innerHTML = html;
    if (role === 'user' || role === 'assistant') li.insertAdjacentHTML('afterbegin', `<span class="rd-msg-who">${role === 'user' ? 'You' : 'Analyst'} · ${new Date().toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}</span>`);
    list.append(li); scroll();
    return li;
  }
  function scroll() { if (stick) list.scrollTop = list.scrollHeight; else jump.hidden = false; }

  // ---------- audio capture & voice activity detection ----------
  async function startConversation() {
    if (conv.active) return;
    used = true;
    if (!navigator.mediaDevices?.getUserMedia || !window.AudioWorkletNode) { message('error', 'This browser cannot capture audio here. Use the text box instead.'); return; }
    // Create and unlock the output graph while this click still has user activation.
    // Creating it after microphone permission / transcription can leave Chrome's context suspended.
    try { await outputContext().resume(); }
    catch (e) { message('error', `Audio output could not start: ${esc(e.message)}.`); }
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true, channelCount: 1 } });
    } catch (e) { setState('error'); message('error', `Microphone access was not granted (${esc(e.name)}). You can still type questions.`); dockLast('Microphone not available - type instead'); return; }
    audioCtx = new AudioContext({ latencyHint: 'interactive' });
    await audioCtx.audioWorklet.addModule(root.dataset.worklet);
    sourceNode = audioCtx.createMediaStreamSource(stream);
    inAnalyser = audioCtx.createAnalyser(); inAnalyser.fftSize = 1024; inAnalyser.smoothingTimeConstant = 0.6;
    workletNode = new AudioWorkletNode(audioCtx, 'radar-capture');
    workletNode.port.onmessage = e => onFrame(e.data.frame, e.data.rms);
    sourceNode.connect(inAnalyser); sourceNode.connect(workletNode);
    // The worklet must be pulled by the graph; route it through a muted gain so nothing is played back.
    const silent = audioCtx.createGain(); silent.gain.value = 0; workletNode.connect(silent).connect(audioCtx.destination);
    ep = audio.createEndpointer(); pending = null; continuation = null; Object.assign(barge, { ms: 0, recent: [], recentMs: 0 });
    setLive(true); setState('listening');
    message('system', 'Live conversation started. Speak naturally; I answer when you pause. Press Esc or Stop to interrupt me.');
    dockLast('Listening - speak naturally');
    fetch(url('Warm'), { method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: csrf } }).catch(() => { });
  }

  function endConversation(silent = false) {
    if (!conv.active && !stream) return;
    interrupt(true); closeReview(); pending = null; continuation = null; ep = null;
    stream?.getTracks().forEach(t => t.stop()); stream = null;
    try { workletNode?.disconnect(); sourceNode?.disconnect(); } catch { /* already disconnected */ }
    audioCtx?.close(); audioCtx = null; workletNode = null; sourceNode = null; inAnalyser = null;
    setLive(false); setState('ended');
    if (!silent) { message('system', 'Conversation ended. The microphone is released.'); dockLast('Conversation ended · microphone released'); }
    setTimeout(() => { if (!conv.active) setState('idle'); }, 1500);
  }

  function onFrame(frame, rms) {
    if (!conv.active || conv.muted || !audioCtx || !ep) return;
    const rate = audioCtx.sampleRate; const frameMs = (frame.length / rate) * 1000;
    if (conv.state === 'speaking') {
      if (!conv.bargein) return; // the analyst's own voice is never treated as a command
      barge.recent.push(frame); barge.recentMs += frameMs;
      while (barge.recentMs > 500 && barge.recent.length > 1) barge.recentMs -= (barge.recent.shift().length / rate) * 1000;
      const loud = rms > Math.max(ep.floor * 7, 0.045);
      barge.ms = loud ? barge.ms + frameMs : Math.max(0, barge.ms - frameMs * 2);
      if (barge.ms > 450) {
        interrupt(false, 'You interrupted - listening.');
        ep.begin(barge.recent, rate); Object.assign(barge, { ms: 0, recent: [], recentMs: 0 });
        setState('listening');
      }
      return;
    }
    // The tail of the analyst's own playback (room echo) never starts an utterance.
    if (performance.now() < conv.deafUntil) return;
    if (conv.state !== 'listening' && conv.state !== 'transcribing' && conv.state !== 'review') return;
    if (conv.state === 'review' && reviewBox.contains(document.activeElement)) return; // the user is correcting the text
    const ev = ep.push(frame, rms, rate);
    if (ev?.type === 'start' && pending) {
      // Still talking: while transcribing, or during the send countdown, the new speech continues the same request.
      // An unclear transcript waiting for confirmation is replaced by what the user says now.
      if (conv.state === 'transcribing' || reviewTimer) continuation = pending.samples;
      pending = null; conv.turn++; closeReview(); setState('listening');
    }
    if (ev?.type === 'end') endUtterance(ev);
  }

  function endUtterance(ev) {
    let samples = audio.resample(ev.samples, ev.rate, 16000);
    if (continuation) { const joined = new Float32Array(continuation.length + 4800 + samples.length); joined.set(continuation); joined.set(samples, continuation.length + 4800); samples = joined; continuation = null; }
    voiceTurn(samples);
  }

  async function voiceTurn(samples) {
    const id = ++conv.turn; const started = performance.now(); const seconds = samples.length / 16000;
    pending = { samples, id };
    setState('transcribing'); dockLast('Transcribing what you said…');
    const form = new FormData(); form.append('audio', new Blob([audio.encodeWav(samples)], { type: 'audio/wav' }), 'utterance.wav'); form.append('language', conv.language);
    let result;
    try {
      const r = await fetch(url('Transcribe'), { method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: csrf }, body: form });
      result = await r.json();
      if (!r.ok) throw new Error(result.error || `Transcription failed (${r.status})`);
    } catch (e) {
      if (id !== conv.turn) return;
      pending = null;
      message('error', `Speech recognition: ${esc(e.message)} You can type instead.`);
      setState(conv.active ? 'listening' : 'idle'); return;
    }
    if (id !== conv.turn) return;
    if (!result.text) { pending = null; setState('listening'); $('[data-voice-metrics]').textContent = 'No speech recognised - still listening.'; dockLast('No speech recognised - still listening'); return; }
    openReview(result, { id, started, seconds });
  }

  // ---------- checking the transcript before anything runs ----------
  // Every spoken request is shown as editable text. Clear transcripts (server confidence) are sent after a short countdown that
  // any edit, click or new speech cancels; unclear ones, or all of them with "Always confirm", wait for Send.
  const reviewBox = document.createElement('div');
  reviewBox.className = 'oi-heard'; reviewBox.hidden = true; reviewBox.setAttribute('role', 'group'); reviewBox.setAttribute('aria-label', 'What I heard');
  reviewBox.innerHTML = `<div class="oi-heard-head"><b>Heard</b><span data-heard-meta></span></div>
    <textarea data-heard-text rows="2" dir="auto" aria-label="Transcript - edit before sending"></textarea>
    <div class="oi-heard-actions"><span data-heard-note></span><button type="button" class="rd-btn" data-heard="discard">Discard</button><button type="button" class="rd-btn rd-btn-primary" data-heard="send">Send</button></div>
    <i class="oi-heard-timer" aria-hidden="true"></i>`;
  root.append(reviewBox);
  const reviewText = reviewBox.querySelector('[data-heard-text]');
  let reviewTimer = 0; let review = null;
  const languageName = l => (l === 'ar' ? 'Arabic' : l === 'en' ? 'English' : 'unknown language');
  function openReview(result, turn) {
    review = { result, turn };
    const clear = !!result.reliable;
    reviewText.value = result.text; reviewText.dataset.original = result.text;
    reviewBox.querySelector('[data-heard-meta]').textContent = `${languageName(result.language)} · ${clear ? 'clear' : 'unclear - please check'} · ${Math.round((result.confidence || 0) * 100)}%`;
    reviewBox.classList.toggle('is-unclear', !clear);
    const auto = clear && conv.review === 'auto';
    reviewBox.querySelector('[data-heard-note]').textContent = auto ? 'Sending… edit or keep talking to change it' : clear ? 'Press Send or Enter' : 'Correct it if needed, then Send - or just say it again';
    reviewBox.hidden = false; reviewBox.classList.remove('is-counting'); void reviewBox.offsetWidth;
    clearTimeout(reviewTimer); reviewTimer = 0;
    if (auto) { reviewBox.classList.add('is-counting'); reviewTimer = setTimeout(() => { reviewTimer = 0; sendReview(); }, 2200); }
    setState('review'); dockLast(`Heard: “${result.text.slice(0, 70)}”${auto ? '' : ' - check and send'}`);
  }
  function holdReview() { if (reviewTimer) { clearTimeout(reviewTimer); reviewTimer = 0; reviewBox.classList.remove('is-counting'); reviewBox.querySelector('[data-heard-note]').textContent = 'Press Send or Enter'; } }
  function closeReview() { clearTimeout(reviewTimer); reviewTimer = 0; review = null; reviewBox.hidden = true; reviewBox.classList.remove('is-counting'); }
  function sendReview() {
    if (!review) return;
    const { result, turn } = review; const text = reviewText.value.trim(); const edited = text !== reviewText.dataset.original;
    closeReview(); pending = null;
    if (!text) { setState(conv.active ? 'listening' : 'idle'); return; }
    ask(text, { spoken: true, language: edited ? null : result.language, sttMs: result.processingMs, audioSeconds: turn.seconds, turnStart: turn.started, confidence: result.confidence, edited });
  }
  reviewText.addEventListener('focus', holdReview);
  reviewText.addEventListener('input', holdReview);
  reviewText.addEventListener('keydown', e => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendReview(); }
    else if (e.key === 'Escape') { e.stopPropagation(); closeReview(); pending = null; setState(conv.active ? 'listening' : 'idle'); }
  });
  reviewBox.addEventListener('click', e => {
    const b = e.target.closest('[data-heard]'); if (!b) return;
    if (b.dataset.heard === 'send') sendReview();
    else { closeReview(); pending = null; setState(conv.active ? 'listening' : 'idle'); dockLast('Discarded - listening'); }
  });

  // ---------- asking the analyst (streamed) ----------
  async function ask(text, options = {}) {
    text = (text || '').trim(); if (!text) return;
    used = true;
    if (review) { closeReview(); pending = null; }
    if (conv.controller || ttsQueue.length || currentSource) interrupt(false);
    radar()?.cancelAnimations();
    const id = ++conv.turn; const controller = new AbortController(); conv.controller = controller;
    const started = options.turnStart ?? performance.now();
    stick = true;
    message('user', esc(text));
    const bubble = message('assistant', '', 'is-streaming');
    const body = document.createElement('div'); bubble.append(body);
    const visualsBox = document.createElement('div'); visualsBox.className = 'oi-visuals'; bubble.append(visualsBox);
    const attachment = conv.attachment ? { name: conv.attachment.name, text: conv.attachment.text } : null;
    if (attachment) bubble.insertAdjacentHTML('afterbegin', `<span class="oi-msg-file">📎 ${esc(attachment.name)} · uploaded file, not system records</span>`);
    const tools = []; const actions = []; let raw = ''; const sentences = []; let firstAudio = null;
    setState('analyzing'); dockLast(`Analyzing: “${text.slice(0, 60)}”`);
    const onAction = e => { if (id === conv.turn && e.detail) { actions.push(e.detail); render(); dockLast(e.detail); } };
    window.addEventListener('radar:action', onAction);
    const language = conv.language !== 'auto' ? conv.language : options.language && options.language !== 'auto' ? options.language : null;
    const speak = conv.speaker && (conv.active || options.speakAnyway);
    try {
      const response = await fetch(url('Ask'), {
        method: 'POST', credentials: 'same-origin', signal: controller.signal,
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf },
        // pending: the partly understood request my last question was about; lastReport: the topic last shown.
        body: JSON.stringify({ message: text, context: { ...(radar()?.context() || {}), pending: conv.pending, lastReport: conv.lastReport }, history: conv.history.slice(-6), language, attachment })
      });
      if (!response.ok || !response.body) throw new Error(`The analyst is unavailable (${response.status}).`);
      conv.pending = null;
      const reader = response.body.getReader(); const decoder = new TextDecoder(); let buffer = '';
      for (; ;) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        let nl;
        while ((nl = buffer.indexOf('\n')) >= 0) {
          const line = buffer.slice(0, nl).trim(); buffer = buffer.slice(nl + 1);
          if (!line || id !== conv.turn) continue; // stale turn: ignore every event, including page commands
          const ev = JSON.parse(line);
          if (ev.type === 'delta') { raw += ev.text; render(); }
          else if (ev.type === 'sentence') {
            sentences.push(ev); render();
            if (speak) enqueueSpeech(ev.verified ? ev.speak : (language === 'ar' || /[؀-ۿ]/.test(ev.text) ? 'يحتوي هذا الجزء على رقم لم يمكن التحقق منه، لذلك لم أقرأه.' : 'This part contains a figure I could not verify, so I did not read it aloud.'),
              /[؀-ۿ]/.test(ev.text) ? 'ar' : 'en', id).then(t => { if (t && firstAudio == null) firstAudio = t; });
          }
          else if (ev.type === 'tool') { tools.push(ev); render(); dockLast(`Checked ${ev.name.replace(/_/g, ' ')}`); }
          else if (ev.type === 'command') radar()?.apply(ev.command);
          else if (ev.type === 'navigate') {
            // Only what the page confirms is shown and spoken.
            const shown = await (radar()?.navigate?.(ev.command) ?? Promise.resolve({ ok: false, reason: 'The page is not ready.' }));
            if (id !== conv.turn) continue;
            if (ev.report) conv.lastReport = ev.report;
            const said = shown.ok ? ev.explanation : `I could not show ${shown.label || 'that view'}: ${shown.reason}`;
            const item = { type: 'sentence', text: said, verified: true, unverified: [], speak: shown.ok ? ev.speak : said };
            raw += (raw ? ' ' : '') + said; sentences.push(item); actions.push(shown.ok ? `Showing ${shown.label}` : `Not shown: ${shown.reason}`); render();
            if (speak) enqueueSpeech(item.speak, /[؀-ۿ]/.test(said) ? 'ar' : 'en', id).then(t => { if (t && firstAudio == null) firstAudio = t; });
            dockLast(said.slice(0, 110));
          }
          else if (ev.type === 'pending') conv.pending = ev.intent || null;
          else if (ev.type === 'file') { conv.lastReport = ev.report || conv.lastReport; renderFile(visualsBox, ev); scroll(); }
          else if (ev.type === 'visual' && ev.visual) { if (ev.report) conv.lastReport = ev.report; renderVisual(visualsBox, ev.visual); scroll(); dockLast(`Drew “${ev.visual.title}”`); }
          else if (ev.type === 'error') { bubble.classList.remove('is-streaming'); bubble.classList.add('rd-msg-error'); body.textContent = ev.message; }
          else if (ev.type === 'done') {
            const verified = sentences.every(s => s.verified);
            conv.history.push({ role: 'user', text }, { role: 'assistant', text: ev.text });
            conv.transcript.push({ role: 'user', text, verified: null }, { role: 'assistant', text: ev.text, verified });
            const m = ev.metrics || {};
            metrics({ model: ev.model, firstTokenMs: m.firstTokenMs, tps: m.tokensPerSecond, prompt: m.promptTokens, stt: options.sttMs, audio: options.audioSeconds, confidence: options.confidence, edited: options.edited, tools: ev.toolCalls, started });
            dockLast(String(ev.text || '').replace(/\*\*/g, '').slice(0, 110));
          }
        }
      }
    } catch (e) {
      window.removeEventListener('radar:action', onAction);
      if (e.name === 'AbortError') return;
      bubble.classList.add('rd-msg-error'); body.textContent = e.message;
      setState(conv.active ? 'listening' : 'error'); dockLast(e.message);
      return;
    } finally { if (conv.controller === controller) conv.controller = null; bubble.classList.remove('is-streaming'); }
    window.removeEventListener('radar:action', onAction);
    if (id !== conv.turn) return;
    await drainSpeech();
    if (id !== conv.turn) return;
    if (firstAudio != null) { $('[data-voice-metrics]').textContent += ` · first audio ${Math.round(firstAudio - started)} ms`; conv.deafUntil = performance.now() + 350; }
    ep?.reset();
    setState(conv.active ? 'listening' : 'idle');

    function render() {
      // Verified sentences are plain; sentences with an unverifiable figure are marked and explained.
      const done = sentences.map(s => s.verified ? md(esc(s.text)) : `<span class="rd-unverified" title="Contains a figure not found in the verified data: ${esc(s.unverified.join(', '))}">${esc(s.text)}</span>`).join(' ');
      const rest = raw.length > sentences.reduce((n, s) => n + s.text.length, 0) ? esc(raw.slice(sentences.length ? raw.lastIndexOf(sentences[sentences.length - 1].text) + sentences[sentences.length - 1].text.length : 0)) : '';
      const flagged = sentences.filter(s => !s.verified);
      body.innerHTML = `${done} ${md(rest)}` + (tools.length || actions.length ? `<div class="rd-tools">${tools.map(t => `<span class="${t.ok ? 'is-ok' : 'is-fail'}">${t.ok ? '✓' : '✗'} ${esc(t.name.replace(/_/g, ' '))}</span>`).join('')}${actions.map(a => `<span class="is-action">↳ ${esc(a)}</span>`).join('')}</div>` : '') +
        (flagged.length ? `<span class="rd-flag">⚑ ${flagged.length} sentence(s) contain a figure that could not be verified against Orange Radar data and were not read aloud.</span>` : '');
      scroll();
    }
  }

  function metrics(m) {
    const parts = [m.model, m.audio ? `heard ${m.audio.toFixed(1)} s` : null, m.stt ? `STT ${Math.round(m.stt)} ms` : null, m.confidence != null ? `transcript ${Math.round(m.confidence * 100)}%${m.edited ? ' (edited)' : ''}` : null, m.firstTokenMs ? `first token ${Math.round(m.firstTokenMs)} ms` : null,
      m.tps ? `${m.tps} tok/s` : null, m.prompt ? `${m.prompt} prompt tokens` : null, m.tools ? `${m.tools} tool call(s)` : null, `turn ${Math.round(performance.now() - m.started)} ms`].filter(Boolean);
    $('[data-voice-metrics]').textContent = parts.join(' · ');
  }

  // ---------- speech output (Piper) ----------
  const ttsQueue = []; let currentSource = null; let drainWaiters = []; let finishBrowserSpeech = null;
  function outputContext() {
    outCtx ??= new AudioContext();
    return outCtx;
  }
  function outputAnalyser(ctx) {
    if (!outAnalysers.has(ctx)) { const a = ctx.createAnalyser(); a.fftSize = 1024; a.smoothingTimeConstant = 0.55; a.connect(ctx.destination); outAnalysers.set(ctx, a); }
    return outAnalysers.get(ctx);
  }
  function enqueueSpeech(text, language, turn) {
    if (!text) return Promise.resolve(null);
    const ctx = outputContext();
    const item = { turn, text, language, started: null, resolveStart: null };
    const startedPromise = new Promise(r => (item.resolveStart = r));
    const speed = language === 'ar' ? Math.min(1.6, conv.speed * 1.3) : conv.speed;
    item.audio = conv.ttsAvailable ? fetch(url('Speak'), { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf }, body: JSON.stringify({ text, language, speed }) })
      .then(async r => { if (!r.ok) { const e = await r.json().catch(() => ({})); throw new Error(e.error || `speech ${r.status}`); } return ctx.decodeAudioData(await r.arrayBuffer()); })
      : null;
    ttsQueue.push(item);
    if (!currentSource) playNext();
    return startedPromise;
  }
  async function playNext() {
    const item = ttsQueue.shift();
    if (!item) { currentSource = null; drainWaiters.forEach(r => r()); drainWaiters = []; startDrawing(); return; }
    currentSource = { pending: true };
    if (!item.audio) { const fallback = await browserSpeech(item.text, item.language, item.turn); if (!fallback && item.turn === conv.turn) message('error', 'Voice output is unavailable. Check Piper and your system audio output.'); item.resolveStart(fallback); currentSource = null; playNext(); return; }
    let buffer;
    try { buffer = await item.audio; }
    catch (e) {
      conv.ttsAvailable = false;
      if (item.turn !== conv.turn && item.turn !== 'narration') { item.resolveStart(null); currentSource = null; playNext(); return; }
      const fallback = await browserSpeech(item.text, item.language, item.turn);
      if (!fallback) message('error', `Voice output unavailable: ${esc(e.message)}. Check Piper and your system audio output.`);
      item.resolveStart(fallback); currentSource = null; playNext(); return;
    }
    if (item.turn !== conv.turn && item.turn !== 'narration') { item.resolveStart(null); playNext(); return; }
    const ctx = outputContext();
    if (ctx.state === 'suspended') await ctx.resume().catch(() => { });
    if (ctx.state !== 'running') { message('error', 'Audio output is suspended. Click the voice replies button to enable sound.'); item.resolveStart(null); currentSource = null; playNext(); return; }
    const src = ctx.createBufferSource(); src.buffer = buffer; src.connect(outputAnalyser(ctx));
    currentSource = src;
    src.onended = () => { if (currentSource === src) playNext(); };
    if (conv.state !== 'speaking' && item.turn !== 'narration') setState('speaking');
    src.start(); item.resolveStart(performance.now()); startDrawing();
  }
  function browserSpeech(text, language, turn) {
    if (!('speechSynthesis' in window) || !('SpeechSynthesisUtterance' in window)) return Promise.resolve(null);
    return new Promise(resolve => {
      if (turn !== conv.turn && turn !== 'narration') { resolve(null); return; }
      let started = null;
      const finish = () => { if (finishBrowserSpeech === finish) finishBrowserSpeech = null; resolve(started); };
      finishBrowserSpeech = finish;
      const utterance = new SpeechSynthesisUtterance(text);
      utterance.lang = language === 'ar' ? 'ar-JO' : 'en-US'; utterance.rate = language === 'ar' ? Math.min(1.6, conv.speed * 1.3) : conv.speed;
      const localVoice = speechSynthesis.getVoices().find(v => v.localService && v.lang.toLowerCase().startsWith(language));
      if (!localVoice) { finish(); return; }
      utterance.voice = localVoice;
      utterance.onstart = () => { started = performance.now(); if (turn === conv.turn || turn === 'narration') setState('speaking'); };
      utterance.onend = finish;
      utterance.onerror = () => { finish(); message('error', 'Browser speech output failed. Check your system audio output.'); };
      try { speechSynthesis.speak(utterance); }
      catch { finish(); }
    });
  }
  function drainSpeech() { return !currentSource && !ttsQueue.length ? Promise.resolve() : new Promise(r => drainWaiters.push(r)); }
  function stopSpeaking() {
    ttsQueue.length = 0;
    window.speechSynthesis?.cancel();
    finishBrowserSpeech?.();
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
    if (busy && !silent) { setState('interrupted'); if (note) { $('[data-voice-metrics]').textContent = note; dockLast(note); } setTimeout(() => { if (conv.state === 'interrupted') setState(conv.active ? 'listening' : 'idle'); }, 600); }
  }

  // Briefing narration: resolves when the scene's sentences have been spoken (or immediately if speech is off).
  function narrate(text, language) {
    if (!conv.speaker || !conv.ttsAvailable) return null;
    stopSpeaking();
    const parts = text.match(/[^.!?؟]+[.!?؟]?/g)?.map(s => s.trim()).filter(Boolean) || [text];
    parts.forEach(p => enqueueSpeech(p, language, 'narration'));
    return drainSpeech();
  }

  // ---------- sphere, spectrum & dock: shape follows real analyser data only ----------
  // Listening -> the conversation's microphone; speaking -> Piper playback. Without a signal the sphere only breathes slowly
  // (a state indicator, never imitated audio); while transcribing/analyzing a light orbit shows that work is in progress.
  const sphereCanvas = $('[data-sphere]'); const sph = sphereCanvas.getContext('2d');
  const dockCanvas = dock.querySelector('[data-dock-orb]'); const dockCtx = dockCanvas.getContext('2d');
  const orbButton = $('[data-orb]');
  const meter = $('[data-voice-meter] i');
  const BANDS = 48;
  const bands = new Float32Array(BANDS);
  let samples = new Float32Array(1024); let freq = new Uint8Array(512); let level = 0; let raf = 0; let lastT = performance.now(); let clock = 0;
  const tint = { cur: null };
  const cssVar = n => getComputedStyle(root).getPropertyValue(n).trim();
  const hexRgb = hex => { const h = String(hex).replace('#', ''); const n = parseInt(h.length === 3 ? h.split('').map(c => c + c).join('') : h.slice(0, 6), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; };
  const rgba = (c, a) => `rgba(${c.map(Math.round).join(',')},${a})`;
  function signal() {
    if (currentSource && currentSource.buffer) return outAnalysers.get(audioCtx || outCtx) || null;
    if (conv.active && !conv.muted && conv.state === 'listening') return inAnalyser;
    return null;
  }
  const thinking = () => conv.state === 'transcribing' || conv.state === 'analyzing';
  const surfacesVisible = () => !document.hidden && (analystVisible || dock.dataset.dockHidden === 'false');
  function wantsFrames() { return surfacesVisible() && (!!signal() || thinking() || (!reduced.matches && analystVisible)); }
  function startDrawing() { if (!raf) raf = requestAnimationFrame(draw); }
  function sizeCanvas(c) { const r = c.getBoundingClientRect(); const ratio = Math.min(2, devicePixelRatio || 1); const w = Math.max(1, Math.round(r.width * ratio)), h = Math.max(1, Math.round(r.height * ratio)); if (c.width !== w || c.height !== h) { c.width = w; c.height = h; } return { w, h, ratio, rect: r }; }
  function stateColor() {
    const s = conv.state;
    if (s === 'error') return hexRgb('#d2443a');
    if (s === 'speaking' || (currentSource && currentSource.buffer)) return hexRgb(cssVar('--rd-orange-strong'));
    if (thinking()) return hexRgb(cssVar('--rd-think'));
    if (s === 'listening' && conv.active && !conv.muted) return hexRgb(cssVar('--rd-listen'));
    return hexRgb(cssVar('--rd-orange-strong')).map((v, i) => v * 0.82 + hexRgb(cssVar('--rd-listen'))[i] * 0.18);
  }
  // Particle shell: points on a slowly turning sphere, tinted teal → orange and mixed with the state colour. Their radius is
  // pushed outward only by the real spectrum (microphone while listening, Piper while speaking); flowing ribbons follow it.
  const SHELL = (() => { const n = 760, pts = []; for (let i = 0; i < n; i++) { const y = 1 - (i / (n - 1)) * 2, r = Math.sqrt(1 - y * y), a = i * 2.39996; pts.push([Math.cos(a) * r, y, Math.sin(a) * r]); } return pts; })();
  function paintSphere(c, cx, cy, R, ratio, col, t, opts) {
    const dark = document.documentElement.dataset.theme !== 'light';
    const audio = opts.audio || 0; const amp = opts.amp || 0;
    const teal = hexRgb(cssVar('--rd-listen')), orange = hexRgb(cssVar('--rd-orange-strong'));
    const halo = c.createRadialGradient(cx, cy, R * 0.2, cx, cy, R * (2 + level * 0.6));
    halo.addColorStop(0, rgba(col, (dark ? 0.26 : 0.16) + level * 0.25)); halo.addColorStop(0.5, rgba(col, dark ? 0.07 : 0.045)); halo.addColorStop(1, rgba(col, 0));
    c.fillStyle = halo; c.beginPath(); c.arc(cx, cy, R * (2 + level * 0.6), 0, Math.PI * 2); c.fill();
    const core = c.createRadialGradient(cx - R * 0.28, cy - R * 0.32, R * 0.04, cx, cy, R * 0.98);
    core.addColorStop(0, rgba(col.map(v => v + (255 - v) * 0.55), dark ? 0.5 : 0.42)); core.addColorStop(0.55, rgba(col, dark ? 0.16 : 0.12)); core.addColorStop(1, rgba(col, 0));
    c.fillStyle = core; c.beginPath(); c.arc(cx, cy, R, 0, Math.PI * 2); c.fill();
    c.save(); c.globalCompositeOperation = dark ? 'lighter' : 'source-over';
    // ribbons: three tilted, breathing rings (their bulge is the live spectrum)
    for (let k = 0; k < 3; k++) {
      const tilt = -0.5 + k * 0.55 + Math.sin(t * 0.25 + k) * 0.08; const ct = Math.cos(tilt), st2 = Math.sin(tilt);
      c.beginPath();
      for (let i = 0; i <= 120; i++) {
        const a = (i / 120) * Math.PI * 2; const bi = Math.min(BANDS - 1, Math.floor((Math.abs(Math.sin(a / 2)) * BANDS)));
        const r = R * (1.06 + k * 0.05 + Math.sin(a * 3 + t * (0.7 + k * 0.25)) * amp * 1.6 + bands[bi] * audio * 1.1);
        const ex = Math.cos(a) * r, ey = Math.sin(a) * r * (0.3 + k * 0.08);
        const x = cx + ex * ct - ey * st2, y = cy + ex * st2 + ey * ct;
        if (i) c.lineTo(x, y); else c.moveTo(x, y);
      }
      const g = c.createLinearGradient(cx - R, cy - R, cx + R, cy + R);
      g.addColorStop(0, rgba(teal, 0.55 - k * 0.12)); g.addColorStop(1, rgba(orange, 0.65 - k * 0.12));
      c.strokeStyle = g; c.lineWidth = (1.6 - k * 0.3) * ratio * (opts.small ? 0.7 : 1); c.stroke();
    }
    const n = opts.small ? 240 : SHELL.length; const stride = SHELL.length / n;
    const rotY = t * 0.32, rotX = 0.38 + Math.sin(t * 0.18) * 0.1; const cY = Math.cos(rotY), sY = Math.sin(rotY), cX = Math.cos(rotX), sX = Math.sin(rotX);
    for (let k = 0; k < n; k++) {
      const [x0, y0, z0] = SHELL[Math.floor(k * stride)];
      const bi = Math.min(BANDS - 1, Math.floor((Math.acos(y0) / Math.PI) * BANDS));
      const wob = Math.sin(3 * Math.atan2(z0, x0) + t * 1.1 + y0 * 4) * amp;
      const rr = R * (1 + wob + bands[bi] * audio * 1.35);
      const x1 = x0 * cY - z0 * sY, z1 = x0 * sY + z0 * cY; const y1 = y0 * cX - z1 * sX, z2 = y0 * sX + z1 * cX;
      const depth = (z2 + 1) / 2;
      const mix = Math.min(1, Math.max(0, (x1 - y1 + 1.2) / 2.4));
      const tint = teal.map((v, i) => (v * (1 - mix) + orange[i] * mix) * 0.6 + col[i] * 0.4);
      c.fillStyle = rgba(tint, (0.14 + depth * 0.8) * (dark ? 1 : 0.9));
      const s = (0.5 + depth * 1.35 + bands[bi] * audio * 2.6) * ratio * (opts.small ? 0.75 : 1);
      c.beginPath(); c.arc(cx + x1 * rr, cy + y1 * rr, s, 0, Math.PI * 2); c.fill();
    }
    c.restore();
    // analysis orbit
    if (opts.orbit) for (let k = 0; k < 3; k++) {
      const a = t * (1.6 - k * 0.3) + (k * Math.PI * 2) / 3; const rr = R * (1.36 + k * 0.1);
      c.beginPath(); c.arc(cx, cy, rr, a, a + 0.9); c.lineWidth = (3 - k * 0.6) * ratio; c.lineCap = 'round'; c.strokeStyle = rgba(col, 0.75 - k * 0.18); c.stroke();
      c.beginPath(); c.arc(cx + Math.cos(a + 0.9) * rr, cy + Math.sin(a + 0.9) * rr, (4 - k) * ratio, 0, Math.PI * 2); c.fillStyle = rgba(col, 0.95); c.fill();
    }
  }
  function draw(now = performance.now()) {
    raf = 0;
    const dt = Math.min(64, now - lastT); lastT = now;
    const still = reduced.matches;
    if (!still) clock += dt / 1000;
    const analyser = signal();
    let target = 0;
    if (analyser) {
      if (samples.length !== analyser.fftSize) samples = new Float32Array(analyser.fftSize);
      if (freq.length !== analyser.frequencyBinCount) freq = new Uint8Array(analyser.frequencyBinCount);
      analyser.getFloatTimeDomainData(samples); analyser.getByteFrequencyData(freq);
      let sum = 0; for (let i = 0; i < samples.length; i++) sum += samples[i] * samples[i];
      target = Math.min(1, Math.sqrt(sum / samples.length) * 4.2);
      // Log-spaced bands across the speech range (first ~40% of bins).
      const top = Math.max(BANDS, Math.floor(freq.length * 0.4));
      for (let b = 0; b < BANDS; b++) {
        const i0 = Math.floor(Math.pow(top, b / BANDS)), i1 = Math.max(i0 + 1, Math.floor(Math.pow(top, (b + 1) / BANDS)));
        let m = 0; for (let i = i0; i < i1 && i < freq.length; i++) m = Math.max(m, freq[i]);
        const v = m / 255; bands[b] += (v - bands[b]) * (v > bands[b] ? 0.5 : 0.15);
      }
    } else for (let b = 0; b < BANDS; b++) bands[b] *= 0.85;
    level += (target - level) * 0.25;
    if (!analyser && level < 0.002) level = 0;
    if (meter) meter.style.width = `${Math.round(level * 100)}%`;
    const goal = stateColor();
    tint.cur = tint.cur ? tint.cur.map((v, i) => v + (goal[i] - v) * 0.08) : goal;
    const col = tint.cur; const moving = tint.cur.some((v, i) => Math.abs(v - goal[i]) > 0.6);
    const audioAmp = still ? 0 : 0.18 + level * 0.3;
    const amp = still ? 0 : analyser ? 0.03 + level * 0.06 : thinking() ? 0.05 : 0.028;

    // Analyst scene: sphere centred on the orb button, spectrum below it.
    if (analystVisible) {
      const s = sizeCanvas(sphereCanvas); const ob = orbButton.getBoundingClientRect();
      const cx = (ob.left + ob.width / 2 - s.rect.left) * s.ratio, cy = (ob.top + ob.height / 2 - s.rect.top) * s.ratio;
      const R = ob.width * 0.36 * s.ratio * (1 + (still ? 0 : level * 0.08));
      sph.clearRect(0, 0, s.w, s.h);
      paintSphere(sph, cx, cy, R, s.ratio, col, clock, { amp, audio: analyser ? audioAmp : 0, orbit: thinking() && !still });
      if (stageEl.classList.contains('is-bar')) {
        // Bar layout: a waveform flowing from the orb across the bar. Its height is the real spectrum; with no signal it is a calm line.
        // Runs between the caption and the controls, never through text.
        const ctrl = stageEl.querySelector('.rd-voice-controls').getBoundingClientRect(); const cap = stageEl.querySelector('.rd-voice-hint').getBoundingClientRect();
        const x0 = Math.max(cx + R * 2.1, (Math.min(cap.right, s.rect.left + s.rect.width * 0.62) - s.rect.left + 28) * s.ratio), x1 = (ctrl.left - s.rect.left - 24) * s.ratio; const span = Math.max(1, x1 - x0);
        for (let k = 0; k < 3; k++) {
          sph.beginPath();
          for (let x = 0; x <= span; x += 3 * s.ratio) {
            const u = x / span; const bi = Math.min(BANDS - 1, Math.floor(u * BANDS)); const env = Math.sin(Math.PI * u);
            const a = bands[bi] * (s.h * 0.34) * env * (1 - k * 0.28);
            const y = cy + Math.sin(u * (9 + k * 3) * Math.PI + clock * (2.4 + k * 0.7) + k) * a;
            if (x) sph.lineTo(x0 + x, y); else sph.moveTo(x0 + x, y);
          }
          sph.lineWidth = (2.2 - k * 0.5) * s.ratio;
          const g = sph.createLinearGradient(x0, 0, x1, 0); g.addColorStop(0, rgba(col, 0)); g.addColorStop(0.18, rgba(col, 0.75 - k * 0.2)); g.addColorStop(0.7, rgba(k === 1 ? hexRgb(cssVar('--rd-listen')) : col, 0.55 - k * 0.15)); g.addColorStop(1, rgba(col, 0));
          sph.strokeStyle = g; sph.stroke();
        }
      } else {
        // mirrored spectrum ribbon (real bands; a flat hairline when there is no signal)
        const baseY = cy + R * 1.62; const width = Math.min(s.w * 0.8, R * 3.4); const x0 = cx - width / 2; const step = width / (BANDS * 2);
        for (let i = 0; i < BANDS * 2; i++) {
          const v = bands[i < BANDS ? BANDS - 1 - i : i - BANDS];
          const h = Math.max(1.5 * s.ratio, v * R * 0.42);
          sph.fillStyle = rgba(col, 0.25 + v * 0.7);
          sph.fillRect(x0 + i * step + step * 0.22, baseY - h / 2, Math.max(1, step * 0.56), h);
        }
      }
    }
    // Dock: the same sphere, small.
    if (dock.dataset.dockHidden === 'false') {
      const d = sizeCanvas(dockCanvas); dockCtx.clearRect(0, 0, d.w, d.h);
      paintSphere(dockCtx, d.w / 2, d.h / 2, d.w * 0.27 * (1 + level * 0.12), d.ratio, col, clock, { amp: amp * 1.2, audio: analyser ? audioAmp : 0, orbit: thinking() && !still, small: true });
    }
    if (wantsFrames() || level > 0.002 || moving) raf = requestAnimationFrame(draw);
  }
  document.addEventListener('visibilitychange', startDrawing);
  window.addEventListener('orange:themechange', () => { tint.cur = null; requestAnimationFrame(draw); });
  reduced.addEventListener?.('change', startDrawing);
  new ResizeObserver(() => startDrawing()).observe(sphereCanvas);

  // ---------- suggestions ----------
  function suggestions() {
    const sel = radar()?.selected || [];
    const n = id => radar()?.name(id) || '';
    const items = sel.length >= 2 ? [`Compare ${n(sel[0])} and ${n(sel[1])}.`, 'Are their documents similar?', `Show the relationship path between ${n(sel[0])} and ${n(sel[1])}.`, `What happens if ${n(sel[0])} is unavailable?`]
      : sel.length === 1 ? [`Why is ${n(sel[0])} connected to other suppliers?`, `Who often competes with ${n(sel[0])}?`, `Show ${n(sel[0])}'s historical prices.`, `What happens if ${n(sel[0])} is unavailable?`]
        : ['Show all suppliers by category.', 'Who competes most often?', 'Show price and timing patterns.', 'Play the timeline.', 'حلل سوق الموردين لدينا'];
    items.push('Generate a report about what we discussed.');
    $('[data-suggestions]').innerHTML = items.map(t => `<button type="button" data-suggest="${esc(t)}" dir="auto">${esc(t)}</button>`).join('');
  }
  window.addEventListener('radar:selection', suggestions);
  window.addEventListener('radar:data', suggestions);

  // ---------- controls ----------
  $('[data-ask-form]').addEventListener('submit', e => { e.preventDefault(); const input = $('[data-ask-input]'); const t = input.value; input.value = ''; ask(t); });
  function toggleMute(button) {
    conv.muted = !conv.muted;
    root.querySelectorAll('[data-voice="mute"], [data-dock="mute"]').forEach(b => { b.setAttribute('aria-pressed', conv.muted); b.setAttribute('aria-label', conv.muted ? 'Unmute microphone' : 'Mute microphone'); });
    stream?.getAudioTracks().forEach(t => (t.enabled = !conv.muted));
    setState(conv.state); void button;
  }
  root.addEventListener('click', e => {
    const b = e.target.closest('button'); if (!b) return;
    if (b.dataset.suggest) ask(b.dataset.suggest);
    else if (b.dataset.action === 'live' || b.hasAttribute('data-orb') || b.dataset.dock === 'live') { conv.active ? endConversation() : startConversation(); }
    else if (b.dataset.voice === 'stop' || b.dataset.dock === 'stop') { interrupt(false, 'Stopped speaking.'); if (radar()?.briefingActive) radar().pauseBriefing(); }
    else if (b.dataset.voice === 'end') endConversation();
    else if (b.dataset.voice === 'mute' || b.dataset.dock === 'mute') toggleMute(b);
    else if (b.dataset.dock === 'open') { radar()?.goScene('analyst'); setTimeout(() => $('[data-ask-input]').focus({ preventScroll: true }), 500); }
    else if (b.dataset.voice === 'speaker') { conv.speaker = !conv.speaker; b.setAttribute('aria-pressed', conv.speaker); b.setAttribute('aria-label', conv.speaker ? 'Voice replies on' : 'Voice replies off'); if (!conv.speaker) stopSpeaking(); else outputContext().resume().catch(e => message('error', `Audio output could not start: ${esc(e.message)}.`)); }
  });
  $('[data-rd-voice-language]').addEventListener('change', e => (conv.language = e.target.value));
  $('[data-voice-review]')?.addEventListener('change', e => (conv.review = e.target.value));
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
    $('[data-voice-model]').innerHTML = [[s.chatModel, s.chatModelInstalled && s.ollama === 'ready'], [`Whisper ${s.whisper}`, conv.sttAvailable], [`Piper ${conv.ttsAvailable ? 'ready' : 'unavailable'}`, conv.ttsAvailable]]
      .map(([t, ok]) => `<span class="rd-comp ${ok ? 'is-ok' : 'is-off'}">${esc(t)}${s.chatModel === t && !s.chatModelInstalled ? ' (not installed)' : ''}</span>`).join('') + `<span class="rd-comp">Local · no external AI</span>`;
  }).catch(() => { $('[data-voice-model]').textContent = 'Status unavailable'; });

  // ---------- generated visuals (allowlisted components only) ----------
  const isDark = () => document.documentElement.dataset.theme !== 'light';
  const num = v => (v == null ? '—' : Number(v).toLocaleString('en-US', { maximumFractionDigits: 2 }));
  function renderVisual(box, v, origin = 'system') {
    const kinds = ['bar', 'line', 'pie', 'table'];
    if (!v || !kinds.includes(v.kind) || typeof v.title !== 'string') return;
    const card = document.createElement('figure'); card.className = `oi-gen is-${origin}`;
    const canChart = v.kind !== 'table' && Array.isArray(v.labels) && v.labels.length && Array.isArray(v.series) && v.series.length;
    const cols = canChart ? ['', ...v.series.map(x => x.name)] : v.columns || [];
    const rows = canChart ? v.labels.map((l, i) => [l, ...v.series.map(x => num(x.values[i]))]) : v.rows || [];
    card.innerHTML = `<figcaption><div><b>${esc(v.title)}</b>${v.subtitle ? `<small>${esc(v.subtitle)}</small>` : ''}</div>
      <div class="oi-gen-actions">${canChart ? '<button type="button" data-gen="toggle">Table</button>' : ''}<button type="button" data-gen="xlsx">Excel</button>${canChart ? '<button type="button" data-gen="png">PNG</button>' : ''}<button type="button" data-gen="pin" title="Keep on the AI Studio board">Pin</button></div></figcaption>
      ${canChart ? `<div class="rd-chat-chart" style="height:${Math.min(420, Math.max(220, (v.kind === 'bar' && v.labels.length > 6 ? v.labels.length * 30 : 260)))}px"></div>` : ''}
      <div class="oi-gen-table" ${canChart ? 'hidden' : ''}><table><thead><tr>${cols.map(c => `<th>${esc(c)}</th>`).join('')}</tr></thead><tbody>${rows.map(r => `<tr>${r.map((c, i) => `<${i ? 'td' : 'th'}>${esc(c)}</${i ? 'td' : 'th'}>`).join('')}</tr>`).join('')}</tbody></table></div>
      <p class="oi-gen-source">${origin === 'upload' ? '📎 From the uploaded file - not verified against system records.' : `Source: ${esc(v.source || 'verified records')}`}${v.note ? ` · ${esc(v.note)}` : ''}</p>`;
    box.append(card);
    let chart = null;
    if (canChart && window.echarts) {
      const el = card.querySelector('.rd-chat-chart');
      chart = echarts.init(el, null, { renderer: 'canvas' });
      new ResizeObserver(() => chart.resize()).observe(el);
      const css = n => getComputedStyle(root).getPropertyValue(n).trim();
      const ink = css('--rd-ink'), muted = css('--rd-muted'), line = css('--rd-line'), soft = css('--rd-line-soft'), surface = css('--rd-surface');
      const colors = [css('--rd-orange-strong'), css('--c2'), css('--c1'), css('--c3'), css('--c5'), css('--c6')];
      const horizontal = v.kind === 'bar' && (v.labels.length > 6 || v.labels.some(l => String(l).length > 14));
      const catAxis = { type: 'category', data: v.labels, axisLine: { lineStyle: { color: line } }, axisTick: { show: false }, axisLabel: { color: ink, fontSize: 13.5, width: horizontal ? 150 : 110, overflow: 'truncate', interval: 0, hideOverlap: !horizontal } };
      const valAxis = { type: 'value', name: v.unit || '', nameTextStyle: { color: muted, fontSize: 13 }, axisLabel: { color: muted, fontSize: 13 }, splitLine: { lineStyle: { color: soft, type: [4, 6] } } };
      const tooltip = { trigger: v.kind === 'pie' ? 'item' : 'axis', confine: true, backgroundColor: surface, borderColor: line, textStyle: { color: ink, fontSize: 14 } };
      chart.setOption(v.kind === 'pie' ? {
        tooltip, color: colors, series: [{ type: 'pie', radius: ['45%', '72%'], padAngle: 2, itemStyle: { borderRadius: 8, borderColor: surface, borderWidth: 2 }, label: { color: ink, fontSize: 13.5, formatter: '{b}\n{d}%' },
          data: v.labels.map((l, i) => ({ name: l, value: v.series[0].values[i] })) }]
      } : {
        tooltip, color: colors, animationDuration: 600, legend: v.series.length > 1 ? { top: 0, textStyle: { color: ink, fontSize: 13.5 }, icon: 'circle', itemWidth: 10 } : undefined,
        grid: { left: 8, right: horizontal ? 56 : 18, top: v.series.length > 1 ? 34 : 14, bottom: 8, containLabel: true },
        xAxis: horizontal ? valAxis : catAxis, yAxis: horizontal ? { ...catAxis, inverse: true } : valAxis,
        series: v.series.map((x, k) => ({ name: x.name, type: v.kind, data: x.values, smooth: 0.3, symbolSize: 8, barMaxWidth: 26, barGap: '20%',
          itemStyle: { borderRadius: horizontal ? [0, 8, 8, 0] : [8, 8, 0, 0] }, lineStyle: { width: 3 },
          label: { show: v.series.length === 1 && v.labels.length <= 12, position: horizontal ? 'right' : 'top', color: ink, fontSize: 13, fontWeight: 700, formatter: d => num(d.value) },
          areaStyle: v.kind === 'line' && v.series.length === 1 ? { opacity: isDark() ? 0.18 : 0.12 } : undefined, z: 2 - k }))
      });
    }
    card.addEventListener('click', e => {
      const b = e.target.closest('[data-gen]'); if (!b) return;
      if (b.dataset.gen === 'toggle') { const t = card.querySelector('.oi-gen-table'); const c = card.querySelector('.rd-chat-chart'); t.hidden = !t.hidden; c.hidden = !t.hidden; b.textContent = t.hidden ? 'Table' : 'Chart'; if (t.hidden) chart?.resize(); }
      else if (b.dataset.gen === 'xlsx') { if (origin !== 'upload' && v.spec) radar()?.exportVisual?.(v.spec); else radar()?.exportTable?.(v.title, cols.map(c => c || 'Label'), rows, 'Uploaded file (not system records)'); }
      else if (b.dataset.gen === 'png' && chart) { const a = document.createElement('a'); a.href = chart.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: isDark() ? '#0d1626' : '#ffffff' }); a.download = `${v.title.replace(/[^\w\- ]+/g, '').slice(0, 60) || 'chart'}.png`; a.click(); }
      else if (b.dataset.gen === 'pin') { b.disabled = true; b.textContent = 'Pinned'; card.classList.add('is-pinned'); radar()?.toast?.('Pinned in this conversation (this page only).'); }
    });
    return card;
  }

  // What a generated file actually contains (from the server's build result), next to the answer that describes it.
  function renderFile(box, f) {
    const card = document.createElement('figure'); card.className = 'oi-gen oi-file-card';
    const kind = f.format === 'xlsx' ? 'Excel' : f.format === 'docx' ? 'Word' : 'PDF';
    card.innerHTML = `<figcaption><div><b>${esc(f.title)}</b><small>${kind} · ${esc(f.scope)}</small></div></figcaption>
      <div class="oi-gen-table"><table><thead><tr><th>${f.format === 'xlsx' ? 'Sheet' : 'Section'}</th><th>Rows</th></tr></thead><tbody>${(f.sheets || []).map(x => `<tr><th>${esc(x.title)}</th><td>${num(x.rows)}</td></tr>`).join('')}</tbody></table></div>`;
    box.append(card);
  }

  // ---------- uploads (PDF / XLSX / CSV / TXT): extracted on the server, kept in this page only ----------
  const upload = root.querySelector('[data-upload]');
  const attachBox = root.querySelector('[data-attachment]');
  upload?.addEventListener('change', async () => {
    const file = upload.files?.[0]; upload.value = ''; if (!file) return;
    attachBox.hidden = false; attachBox.innerHTML = `<p class="rd-muted">Reading ${esc(file.name)}…</p>`;
    const form = new FormData(); form.append('file', file);
    try {
      const r = await fetch(url('Extract'), { method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: csrf }, body: form });
      const data = await r.json(); if (!r.ok) throw new Error(data.error || `Upload failed (${r.status})`);
      conv.attachment = data; used = true; renderAttachment();
    } catch (e) { conv.attachment = null; attachBox.innerHTML = `<p class="rd-msg-error">${esc(e.message)}</p>`; }
  });
  function numericColumns(a) { return (a.columns || []).map((c, i) => ({ c, i })).filter(({ i }) => { const vals = a.rows.map(r => r[i]).filter(x => String(x).trim() !== ''); return vals.length > 0 && vals.filter(x => !Number.isNaN(Number(String(x).replace(/,/g, '')))).length / vals.length > 0.8; }); }
  function renderAttachment() {
    const a = conv.attachment; if (!a) { attachBox.hidden = true; attachBox.innerHTML = ''; return; }
    const nums = numericColumns(a);
    const preview = a.columns.length ? `<div class="oi-att-preview"><table><thead><tr>${a.columns.slice(0, 8).map(c => `<th>${esc(c)}</th>`).join('')}</tr></thead><tbody>${a.rows.slice(0, 6).map(r => `<tr>${r.slice(0, 8).map(c => `<td>${esc(c)}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`
      : `<pre class="oi-att-text" dir="auto">${esc(a.text.slice(0, 900))}${a.text.length > 900 ? '…' : ''}</pre>`;
    attachBox.hidden = false;
    attachBox.innerHTML = `<div class="oi-att-head"><span class="oi-att-kind">${esc(a.kind)}</span><div><b>${esc(a.name)}</b><small>${a.columns.length ? `${a.totalRows} rows · ${a.columns.length} columns` : `${a.text.length.toLocaleString('en-US')} characters`}${a.notice ? ` · ${esc(a.notice)}` : ''} · attached to your next questions</small></div>
      <button type="button" class="rd-icon-btn" data-att="remove" aria-label="Remove attachment"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18"/></svg></button></div>${preview}
      <div class="oi-att-actions">${nums.length ? `<label>Chart <select data-att-col>${nums.map(n => `<option value="${n.i}">${esc(n.c)}</option>`).join('')}</select></label><button type="button" class="rd-btn" data-att="chart">Draw</button>` : ''}
      ${a.columns.length ? '<button type="button" class="rd-btn" data-att="xlsx">Export Excel</button>' : ''}<button type="button" class="rd-btn" data-att="ask">Summarise it</button></div>`;
  }
  attachBox?.addEventListener('click', e => {
    const b = e.target.closest('[data-att]'); if (!b) return; const a = conv.attachment; if (!a) return;
    if (b.dataset.att === 'remove') { conv.attachment = null; renderAttachment(); }
    else if (b.dataset.att === 'xlsx') radar()?.exportTable?.(a.name, a.columns, a.rows, 'Uploaded file (not system records)');
    else if (b.dataset.att === 'ask') ask(a.columns.length ? `Summarise the attached file "${a.name}": what does it contain and what stands out? Compare with our records only where they match.` : `Summarise the attached document "${a.name}" and list its key requirements, dates and figures.`);
    else if (b.dataset.att === 'chart') {
      const col = +attachBox.querySelector('[data-att-col]').value; const labelCol = a.columns.findIndex((_, i) => !numericColumns(a).some(n => n.i === i));
      const rows = a.rows.filter(r => String(r[col]).trim() !== '').slice(0, 24);
      const li = message('assistant', `<p>Chart of <b>${esc(a.columns[col])}</b> from <b>${esc(a.name)}</b> (first ${rows.length} rows).</p>`);
      renderVisual(li, { kind: 'bar', title: `${a.columns[col]} · ${a.name}`, subtitle: `${rows.length} rows from the uploaded file`, unit: '', labels: rows.map((r, i) => (labelCol >= 0 ? String(r[labelCol]).slice(0, 40) : `Row ${i + 1}`)), series: [{ name: a.columns[col], values: rows.map(r => Number(String(r[col]).replace(/,/g, ''))) }], source: a.name }, 'upload');
      radar()?.setTab?.('studio'); scroll();
    }
  });

  window.OrangeRadarVoice = Object.freeze({
    ask: t => ask(t),
    prefill: t => { radar()?.goScene('analyst'); const i = $('[data-ask-input]'); i.value = t; setTimeout(() => i.focus({ preventScroll: true }), 450); },
    renderVisual: (box, v) => renderVisual(box, v),
    narrate, stopSpeaking, transcript: () => conv.transcript.slice(-30),
    language: () => conv.language
  });
  setState('idle');
  requestAnimationFrame(draw);
})();
