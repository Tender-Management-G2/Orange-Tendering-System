# ORANGE RADAR — AI-Powered Supplier Market Intelligence

*See the market. Speak to your data.*

Route: **`/SourcingOfficer/MarketIntelligence`** (sidebar: **Market Intelligence**). Sourcing Officer role only.

"The market" means the suppliers and procurement activity recorded in this system, never the external supplier market.

## What it does

| Area | What the officer gets |
|---|---|
| Market summary | Registered vs eligible suppliers, suppliers that bid, analysed tenders/rounds, submitted offers, average offers per round/tender, historical awarded value **per currency**, detected relationships, review signals. Every tile has its definition as a tooltip. |
| Market Overview | Supplier landscape (participation × win rate, bubble = awarded value, colour = category), competition per round (invited vs submitted, minimum line), award distribution (per currency, HHI), Supplier DNA radar, competition by department, recurring competitor pairs. Each chart has a table view. |
| Supplier Network | Cytoscape graph: suppliers, tenders (expand on demand), shared-contact and document nodes; fact / context / signal edges with distinct colours and line styles; reveal animation, dynamic focus, evidence-path animation, group formation, shortest path, search, filters, multi-select (Shift+click), PNG export, full screen, relationship table. |
| Price Intelligence | Relative price position over time (offer total ÷ round median − 1) and unit prices per tender item (same item, quantity, unit, currency and round). |
| Forensic Intelligence | 8 detectors with state (analysed / insufficient data / not run), findings with evidence records, method, sample size, period and limitations; Document DNA (SHA-256 + local embeddings) with side-by-side passage comparison. |
| Context panel | Details (supplier profile, DNA, comparison), Evidence, Scenario (hypothetical dependency simulator), Analyst (voice + text). Resizable and collapsible. |
| Timeline | Historical replay of offers (shown when bidding closed, never at private submission time), awards and patterns; play/pause/speed/scrub/reset. |
| Brief Me | Guided story of verified scenes (mode, suppliers, graph edges, evidence, camera) with narration; play/pause/previous/next/replay/stop and "Ask about this". |
| Export report | PDF intelligence report with charts and network images, statistics, findings, alternative explanations, limitations, conversation record and an AI interpretation whose sentences passed the figure check. Separate from official evaluation reports. |

## Architecture (no new tables, no migrations)

```
Services/Radar/
  RadarOptions.cs          configuration (models, thresholds, Whisper, Piper, demo dataset)
  RadarSnapshot.cs         read-only snapshot of the period + sealing rule, 60 s memory cache
  RadarAnalytics.cs        market metrics, landscape, competition, awards, DNA, prices, simulator
  RadarForensics.cs        the 8 detectors (pure LINQ, stable finding ids)
  RadarGraph.cs            Cytoscape nodes/edges (stable ids, no duplicates)
  RadarDocumentDna.cs      SHA-256, text extraction (ProtectedDocumentStore), embeddings cache, cosine
  RadarService.cs          facade used by the page, AI tools, briefing and report
  RadarOllamaClient.cs     loopback-only streaming /api/chat with tools, /api/embed, warm-up
  RadarAnalystService.cs   context-aware analyst: tool registry, validation, streaming, sentence verification
  RadarGrounding.cs        numeric grounding guard + sentence splitting + speakable text
  RadarBriefingService.cs  "Brief Me" scenes (deterministic) + optional AI wording (verified)
  RadarReportService.cs    MigraDoc PDF (reuses the report font resolver)
  RadarSpeech.cs           Whisper.net recognizer + persistent Piper worker manager
  RadarDemoSeeder.cs       fictional demonstration market (isolated database only)
Areas/SourcingOfficer/Controllers/MarketIntelligenceController.cs
Areas/SourcingOfficer/Views/MarketIntelligence/Index.cshtml
wwwroot/js/orange-radar.js, orange-radar-voice.js, orange-radar-capture.js (AudioWorklet)
wwwroot/css/orange-radar.css
wwwroot/lib/cytoscape (3.30.4, MIT), wwwroot/lib/echarts (5.6.0, Apache-2.0)
Tools/OrangeRadar/radar_piper_worker.py
Tests/RadarChecks
```

Data rules:

* **Sealing.** Offers are analysed only after bidding closed (round Closed / Failed / SentToEvaluation, or deadline passed). Offers in open rounds are counted as "sealed, excluded" and are never priced, graphed or sent to the model.
* **Effective offer** = latest submitted revision per invitation; superseded/withdrawn versions are ignored.
* **Offer total** = Σ quantity × unit price + shipping + installation + maintenance + tax + other.
* **Awarded value** = total of `Tender.AwardedBidId` (official awards only, never recommendations), grouped by currency, never summed across currencies.
* **Win rate** = awards ÷ awarded tenders the supplier bid on (undefined when 0). **Response rate** = offers ÷ closed-round invitations.
* Departments are used as tender groups because tenders carry no category.

## Detectors and thresholds (`OrangeRadar:Thresholds`)

| # | Detector | Rule (defaults) | Level |
|---|---|---|---|
| 1 | Shared contact information | Same normalized phone (last 9 digits), e-mail, tax number → signal; same company e-mail domain or address (case/punctuation-insensitive) → context. Public mail domains (gmail, outlook…) ignored. | signal / context |
| 2 | Repeated joint participation | Both suppliers bid in ≥ 3 closed rounds. Always **context** — normal competition. | context |
| 3 | Repeated relative price gap | ≥ 3 shared rounds, same currency, one supplier always higher, mean ratio 1.005–1.15, coefficient of variation ≤ 2 %. | signal |
| 4 | Historical price similarity | ≥ 3 identical-item lines (same round, item, quantity, unit, currency) within 0.5 %, across ≥ 2 rounds. | signal |
| 5 | Repeated award rotation | ≥ 3 official awards where the same group (2–3 suppliers) all bid, only group members won and the winner changed every time. | signal |
| 6 | Identical documents | Same SHA-256 from ≥ 2 suppliers; files identical to a document issued with the tender are templates and skipped. | signal |
| 7 | Semantic document similarity | qwen3-embedding passages, cosine ≥ **0.85** after removing passages ≥ 0.88 similar to the tender's own text (description, requirements, tender documents). | signal |
| 8 | Submission-time proximity | Latest revisions submitted ≤ 10 min apart in ≥ 3 shared rounds. | signal |

The 0.85 semantic threshold was measured on the demo documents with `qwen3-embedding:0.6b`: re-worded passages 0.881, unrelated proposals 0.45–0.53, verbatim tender text 0.966 (removed as template). Re-measure if the embedding model changes. Every detector reports "insufficient data" instead of an empty result when its sample is too small. A review signal is **not** evidence of wrongdoing; each finding carries its limitations / alternative explanation.

## AI analyst

* Model: `qwen3.5:4b` via local Ollama, `think: false`, streaming, 8192-token context (the tool schemas + page state + history need ~2.3–3.5 k prompt tokens, so 4096 would truncate), keep-alive 15 min, warmed when a live conversation starts.
* Tools — data (server): `market_overview`, `supplier_profile`, `compare_suppliers`, `relationships`, `evidence`, `compare_documents`, `search_documents`, `simulate_unavailable`; visual (client commands): `show_view`, `highlight`, `show_path`, `set_period`, `timeline`, `start_briefing`, `generate_report`.
* Every argument is validated against a strict schema and every id against the snapshot; unknown tools, wrong types, out-of-range or over-long values are rejected and reported back to the model. The browser executes only whitelisted commands — never model-generated code, HTML or SQL. All tools are read-only.
* The page sends structured context (mode, selection, finding, chart point, filters, scenario), never screenshots.
* **Grounding guard:** each streamed sentence is checked; a figure not found in the tool results / page context / user's words (allowing rounding and %↔fraction) is marked in the transcript and that sentence is not read aloud. Document text is passed as `untrustedText` and the prompt forbids following it.

## Live conversation

Browser: `getUserMedia` (echo cancellation, noise suppression) → AudioWorklet → adaptive-threshold VAD (pre-roll 360 ms, end after 850 ms silence, max 15 s) → 16 kHz WAV → `/Transcribe` (Whisper.net) → `/Ask` NDJSON stream → verified sentences → `/Speak` (Piper) queue with prefetch → back to listening. States: idle, listening, transcribing, analyzing, speaking, interrupted, error, ended; the orb and waveform use real input/output audio levels.

* Interruption: **Stop** button / Esc cancels generation, queued speech and pending graph animations. Optional **Barge-in** (experimental): speaking over the analyst interrupts it; relies on browser echo cancellation. Without barge-in the microphone input is ignored while the analyst speaks, so it never answers itself.
* The microphone is requested only on "Start live conversation" and released on end or when the page is left. Audio is processed in memory and never stored.

## Setup for team members

Nothing is required for the analytics; voice and AI degrade gracefully when components are missing (the panel shows each component's state).

1. **Ollama** (local): `ollama pull qwen3.5:4b` and `ollama pull qwen3-embedding:0.6b`. Optional deeper report model: set `OrangeRadar:Models:DeepModel` (e.g. `qwen3:8b`).
2. **Whisper**: download `ggml-small.bin` (multilingual, 466 MB) from `huggingface.co/ggerganov/whisper.cpp` to `%USERPROFILE%\AI-Models\Whisper\` or set `OrangeRadar:Whisper:ModelPath`. NuGet packages `Whisper.net`, `Whisper.net.Runtime` (CPU) and `Whisper.net.Runtime.Vulkan` (GPU via the normal NVIDIA/AMD/Intel driver, no CUDA toolkit) restore automatically. `Runtime`: `auto` (Vulkan, else CPU) or `cpu`.
3. **Piper**: Python 3.12 with `pip install piper-tts`; voices `ar_JO-kareem-medium` and `en_US-lessac-medium` (`.onnx` + `.onnx.json`) in `%USERPROFILE%\AI-Models\Piper\`, or set `OrangeRadar:Piper:PythonExecutable` / `VoicesDirectory`. The app starts `Tools/OrangeRadar/radar_piper_worker.py` itself (stdin/stdout, no port) and stops it on shutdown.
4. Paths accept environment variables (`%USERPROFILE%`, `%LOCALAPPDATA%`); override per machine in User Secrets. Model weights and audio are never committed (App_Data is git-ignored; models live outside the repo).

## Demonstration dataset

Launch profile **`https (Orange Radar demo database)`** uses database `OrangeTenderingTeamDev_RadarDemo` and sets `OrangeRadar:DemoDataset:Enabled=true`. The seeder refuses any database whose name does not end with `_RadarDemo`, so `OrangeTenderingTeamDev` is never touched. It creates 12 fictional "RADAR DEMO" suppliers and 10 tenders containing: normal competition, repeated participation, a shared phone/address pair, a stable 4 % price gap, list-price twins, an identical file, legitimately similar (template) text, a re-worded methodology, award rotation, low-competition rounds, a single-bidder tender, a pending and a suspended supplier, and a sealed open round. The patterns are planted as data; the real detectors find them.

## Measured performance (RTX 3070 Laptop 8 GB, i7-11800H, 2026-11-19)

| Measurement | Result |
|---|---|
| qwen3.5:4b cold load / warm TTFT / generation (think off) | 2.6 s / 0.06 s / 94 tok/s; 100 % GPU, 3.4 GB at 8192 ctx |
| qwen3.5:4b with default thinking | all 200 tokens spent thinking, empty answer → cause of the earlier "slow / no answer" behaviour |
| gemma3:4b | 108 tok/s but answered the Arabic prompt in English |
| qwen3:8b | 10.8 s cold load, 67 tok/s warm, 5.6 GB VRAM |
| Arabic output | correct UTF-8 from the API and in the browser; the malformed text was PowerShell console rendering |
| Tool calling (EN/AR) | correct calls in every benchmark and browser test |
| Analyst turn in the browser (1 tool round) | first token 0.7–1.4 s, total 1.5–2.4 s, 2.1–3.5 k prompt tokens |
| Piper (persistent worker, CPU) | start 1.4 s; 130 ms EN / 260 ms AR per sentence (RTF ≈ 0.03) |
| Whisper small, warm, 2.3 s utterance | CPU 3.0 s full window → **0.44 s** with adaptive audio context; Vulkan 0.43 s |
| Whisper small, Arabic 5.3 s utterance | CPU 3.5 s, Vulkan 2.2 s |
| Browser voice turn (fake mic, Vulkan Whisper) | STT 2.1–2.4 s, analyst 2.3 s to first sentence → first audio ≈ 5 s after the user stops speaking (incl. 0.85 s end-of-speech detection) |
| Document DNA (6 bid docs, first run / cached) | 3.1 s / 0.96 s |

## Limitations

* Text extraction: TXT, CSV and DOCX only (existing extractor); PDFs and images are matched by SHA-256 only.
* The PDF report is English; Arabic conversation turns are referenced, not reproduced (PDFsharp has no Arabic shaping).
* Barge-in depends on browser echo cancellation and was not verified with a real microphone and loudspeakers.
* Speech recognition quality depends on microphone and accent; Whisper small occasionally confuses Arabic words.
* Thresholds are demonstration defaults for a small academic dataset.
