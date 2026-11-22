# Orange Radar checks

Pure analytics checks on an in-memory synthetic market (no database): market metrics, sealing rule, all eight detectors,
insufficient-data states, graph integrity, the numeric grounding guard and AI tool-argument validation.

Run from the web project folder:

```powershell
dotnet run --project Tests/RadarChecks                 # analytics, detectors, guard, validation
dotnet run --project Tests/RadarChecks -- --speech     # + real local Piper -> Whisper round trip (Arabic and English)
dotnet run --project Tests/RadarChecks -- --speech --cpu            # force CPU Whisper
dotnet run --project Tests/RadarChecks -- --speech --full-context   # disable the adaptive audio context
dotnet run --project Tests/RadarChecks -- --scenarios          # user requests -> real analyst turn -> files compared with SQL (local team DB, read-only)
dotnet run --project Tests/RadarChecks -- --intent-bench       # rules vs local models (thinking on/off) on labelled requests (team DB + Ollama)
node Tests/RadarChecks/stt-pipeline.js <corpus>                # replay recordings through the page's speech front end (old vs current)
dotnet run --project Tests/RadarChecks -- --stt-bench <corpus> # Whisper settings on those WAVs: WER/CER, language, latency, confidence flags
```

`--speech` needs the model paths of `OrangeRadar` configuration defaults (see `docs/ORANGE_RADAR.md`).
If the web app is running from `bin/Debug`, build with `-o <folder>` and run the produced `RadarChecks.dll`.
