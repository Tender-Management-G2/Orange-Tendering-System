# Voice UX — reuse Ollama + Whisper + Piper

Prior user setup: Ollama 0.40.16; `qwen3.5:4b`, `qwen3:8b`, `qwen3-embedding:0.6b`, `gemma3:4b`; `Whisper small` via earlier Radar implementation; Piper voices `ar_JO-kareem-medium.onnx`, `en_US-lessac-medium.onnx`. Local NVIDIA RTX 3070 Laptop, 8GB VRAM. **Availability in current repo/session and teammates' machines is not verified here.** Do not bundle weights or voice models; use paths in config, no user-specific absolute path hardcoded.

Single voice state: `idle → listening → transcribing → analyzing → speaking → listening`, with `error` and `stopped`. A singleton voice service persists across tab changes; avoid opening a second microphone. Compact dock never obstructs charts, dropdowns or graph controls. Expanded experience uses same session, same context and transcript.

Animation: sculptural orbital voice orb with real amplitude/frequency from browser Web Audio AnalyserNode; output waveform driven by actual returned audio. Idle breath may be subtle but never fake a microphone signal. Listening / Transcribing / Analyzing / Speaking state visibly distinct, with accessible text, mute, stop, speed, language Auto/AR/EN, privacy indicator. Real barge-in only if working with real hardware; otherwise mark experimental and never claim verification.

AI visual director: let model request **typed UI intents** like `selectSuppliers(ids)`, `switchGraphMode(mode)`, `focusCategory(id)`, `showTender(tenderId)`, `renderAnalysis(spec)`. Backend must authorize every ID and validate every mode. `Follow analyst` preference controls whether AI can scroll/camera-move; no visual action may modify a tender, supplier, award or bid. Confirm with user before any action outside read-only intelligence, but official AI #8 prohibits this page from executing domain actions at all.

Latency: optimize warmed-model availability, token streaming and TTS chunks if already supported, but do not promise 300ms overall. Record actual local benchmarks only with measurement; avoid introducing larger models just for effect.
