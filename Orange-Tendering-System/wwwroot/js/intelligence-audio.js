// ORANGE INTELLIGENCE - AI Studio: Live Voice Call with the Gemini Live API (Views/Intelligence/Index.cshtml).
// 1. Ask our server for a single-use token (IntelligenceController.StartVoice). The real API key never reaches the browser.
// 2. Connect the browser directly to Gemini Live with the official Google SDK.
// 3. Send microphone audio (16-bit PCM, 16 kHz) and play Gemini's spoken reply (16-bit PCM, 24 kHz).
import { GoogleGenAI, Modality } from 'https://cdn.jsdelivr.net/npm/@google/genai@2.32.0/+esm';

const studio = document.querySelector('[data-studio]');
const csrfToken = document.querySelector('[data-radar]')?.dataset.csrf;
const supplierSelect = studio?.querySelector('[data-studio-supplier]');
const startButton = studio?.querySelector('[data-voice-start]');
const endButton = studio?.querySelector('[data-voice-end]');
const voicePanel = studio?.querySelector('[data-voice-panel]');
const statusText = studio?.querySelector('[data-voice-status]');

let callActive = false;      // true from Start Call until the call has ended
let session = null;          // the open Gemini Live connection
let microphone = null;       // the microphone MediaStream
let micContext = null;       // AudioContext that records at 16 kHz
let speakerContext = null;   // AudioContext that plays at 24 kHz
let micAnalyser = null;      // measures how loud the microphone is (for the animation only)
let speakerAnalyser = null;  // measures how loud Gemini's voice is (for the animation only)
let nextPlayTime = 0;        // when the next audio chunk should start, so chunks play back to back
let playingSources = [];     // chunks currently scheduled, so an interruption can stop them
let lastSpeechTime = 0;      // when the user was last heard; 0 = not speaking since the last answer
let thinkingSince = 0;       // when the "thinking" state started

// The panel's data-state picks the CSS animation: idle, connecting, listening, thinking, speaking.
function setState(state, text) {
  voicePanel.dataset.state = state;
  statusText.textContent = text;
  startButton.hidden = state !== 'idle';
  endButton.hidden = state === 'idle';
  if (state === 'listening') lastSpeechTime = 0;
  if (state === 'thinking') thinkingSince = performance.now();
}

// ---------- animation only: audio level and the "thinking" state ----------
const SPEECH_VOLUME = 0.02;   // microphone volume that counts as the user speaking
let shownLevel = 0;

// Returns the volume (root mean square, 0..1) of the last few milliseconds the analyser heard.
function readVolume(analyser) {
  if (!analyser) return 0;
  const samples = new Float32Array(analyser.fftSize);
  analyser.getFloatTimeDomainData(samples);
  let sum = 0;
  for (const sample of samples) sum += sample * sample;
  return Math.sqrt(sum / samples.length);
}

// Runs about 60 times per second during a call: sets --level for the CSS and decides when Gemini is "thinking".
function animateVoice() {
  if (!callActive) {
    voicePanel.style.setProperty('--level', 0);
    return;
  }
  const state = voicePanel.dataset.state;
  const now = performance.now();
  let volume = 0;
  if (state === 'speaking') volume = readVolume(speakerAnalyser);
  if (state === 'listening' || state === 'thinking') volume = readVolume(micAnalyser);

  // The user spoke and then stayed quiet for 0.7 s: Gemini is preparing its answer.
  if (state === 'listening' && volume > SPEECH_VOLUME) lastSpeechTime = now;
  if (state === 'listening' && lastSpeechTime > 0 && now - lastSpeechTime > 700) setState('thinking', 'Thinking…');
  // No answer after 8 s, or the user keeps talking: back to listening.
  if (state === 'thinking' && (now - thinkingSince > 8000 || volume > SPEECH_VOLUME * 2)) setState('listening', 'Listening… speak now');

  // Smooth the level so the orb moves softly instead of flickering.
  const target = state === 'thinking' ? 0 : Math.min(1, volume * 6);
  shownLevel = shownLevel * 0.75 + target * 0.25;
  voicePanel.style.setProperty('--level', shownLevel.toFixed(3));
  requestAnimationFrame(animateVoice);
}

async function startCall() {
  const supplierId = Number(supplierSelect.value);
  if (!supplierId) {
    setState('idle', 'Please choose a supplier first.');
    return;
  }
  callActive = true;
  setState('connecting', 'Connecting to Gemini…');
  try {
    // Step 1: the server checks the supplier is in our scope and returns a short-lived token.
    const response = await fetch(studio.dataset.urlVoice, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrfToken },
      body: JSON.stringify({ supplierId })
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(data.error || `Request failed (${response.status}).`);

    // Step 2: open the Live session. The prompt and supplier facts are already locked inside the token.
    speakerContext = new AudioContext({ sampleRate: 24000 });
    speakerAnalyser = speakerContext.createAnalyser();
    speakerAnalyser.connect(speakerContext.destination);
    const ai = new GoogleGenAI({ apiKey: data.token, httpOptions: { apiVersion: 'v1beta' } });
    session = await ai.live.connect({
      model: data.model,
      config: { responseModalities: [Modality.AUDIO] },
      callbacks: {
        onmessage: handleGeminiMessage,
        onerror: () => endCall('The voice connection failed.'),
        onclose: event => endCall(event.reason ? `Call ended: ${event.reason}` : 'Call ended.')
      }
    });

    if (!callActive) return; // the connection already closed while opening

    // Step 3: start sending the microphone.
    await startMicrophone();
    setState('listening', 'Listening… speak now');
    requestAnimationFrame(animateVoice);
  } catch (error) {
    endCall(error.name === 'NotAllowedError' ? 'Microphone permission was denied.' : error.message);
  }
}

async function startMicrophone() {
  microphone = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true } });
  // A 16 kHz AudioContext lets the browser resample the microphone to the rate Gemini expects.
  micContext = new AudioContext({ sampleRate: 16000 });
  const source = micContext.createMediaStreamSource(microphone);
  const recorder = micContext.createScriptProcessor(4096, 1, 1); // about 0.25 s of audio per chunk

  recorder.onaudioprocess = event => {
    if (!session) return;
    const samples = event.inputBuffer.getChannelData(0); // numbers between -1 and 1
    session.sendRealtimeInput({ audio: { data: floatToPcmBase64(samples), mimeType: 'audio/pcm;rate=16000' } });
  };
  source.connect(recorder);
  recorder.connect(micContext.destination); // required for onaudioprocess to run; the output is silent
  micAnalyser = micContext.createAnalyser(); // only for the orb animation
  source.connect(micAnalyser);
}

// Gemini sends audio as base64 chunks inside serverContent.modelTurn.parts[].inlineData.
function handleGeminiMessage(message) {
  const content = message.serverContent;
  if (!content) return;

  // The user started talking while Gemini was speaking: stop the queued audio at once.
  if (content.interrupted) {
    stopPlayback();
    setState('listening', 'Listening… speak now');
    return;
  }
  for (const part of content.modelTurn?.parts || []) {
    if (part.inlineData?.data) playPcmBase64(part.inlineData.data);
  }
}

function playPcmBase64(base64) {
  const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
  const pcm = new Int16Array(bytes.buffer);
  const buffer = speakerContext.createBuffer(1, pcm.length, 24000);
  const channel = buffer.getChannelData(0);
  for (let i = 0; i < pcm.length; i++) channel[i] = pcm[i] / 32768;

  const source = speakerContext.createBufferSource();
  source.buffer = buffer;
  source.connect(speakerAnalyser); // the analyser passes the sound on to the speakers
  nextPlayTime = Math.max(nextPlayTime, speakerContext.currentTime);
  source.start(nextPlayTime);
  nextPlayTime += buffer.duration;

  playingSources.push(source);
  setState('speaking', 'Gemini is speaking…');
  source.onended = () => {
    playingSources = playingSources.filter(s => s !== source);
    if (playingSources.length === 0 && session) setState('listening', 'Listening… speak now');
  };
}

function stopPlayback() {
  for (const source of playingSources) source.stop();
  playingSources = [];
  nextPlayTime = 0;
}

// Converts browser audio samples (-1..1) to 16-bit little-endian PCM, then to base64 text.
function floatToPcmBase64(samples) {
  const pcm = new Int16Array(samples.length);
  for (let i = 0; i < samples.length; i++) {
    const value = Math.max(-1, Math.min(1, samples[i]));
    pcm[i] = value * 32767;
  }
  let binary = '';
  const bytes = new Uint8Array(pcm.buffer);
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary);
}

// Closes the Gemini session and releases the microphone and both audio contexts.
function endCall(message = 'Call ended.') {
  if (!callActive) return; // already ended (closing the session fires onclose again)
  callActive = false;
  const openSession = session;
  session = null;
  openSession?.close();
  microphone?.getTracks().forEach(track => track.stop());
  micContext?.close();
  speakerContext?.close();
  microphone = micContext = speakerContext = micAnalyser = speakerAnalyser = null;
  playingSources = [];
  nextPlayTime = 0;
  setState('idle', message);
}

if (studio) {
  startButton.addEventListener('click', startCall);
  endButton.addEventListener('click', () => endCall());
  window.addEventListener('pagehide', () => endCall());
}
