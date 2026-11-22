# Reuse the existing system: inspection map and non-destructive plan

The existing app has reportedly implemented Orange Radar: `MarketIntelligence` Razor page, Radar services/analyst, visualizations, C# detectors, Whisper voice input, Piper TTS, Ollama Qwen; older reports mention `wwwroot/js/orange-radar*.js`, `orange-radar.css`, layout `_SourcingOfficerLayout.cshtml`, and shared chatbot. **None of these claims is a verified current repo state.** Use file search/git status and inspect *actual* code before choosing integration path.

1. `git status --short --branch`, `git worktree list`, `git log -3 --oneline`; ensure no one else's uncommitted changes overwritten. Confirm active solution, launch profiles, DB connection names and app processes. Do not switch branches or run migrations without owner approval.
2. Locate `/SourcingOfficer/MarketIntelligence`, any global chatbot service/controller and script modules; inspect their exact permission, tool and DTO contract. Locate role layouts without assuming current role names.
3. Build a **feature reuse inventory** in no more than 1 screen: keep / wrap / adapt / truly missing. Do not rebuild existing analytics, audio engines, exports, document readers, security if functional.
4. Prefer a shared Intelligence MVC controller/service + role-specific authorized data scope, embedding existing Radar services as Suppliers/Forensics. When architecture better supports Razor partials within one route, use it. Avoid a sprawling rewrite.
5. Retain current team database; no new tables unless a mandatory feature cannot be implemented otherwise and user approves. Code-first conventions still apply to any agreed entity change.
6. Ensure the existing global chatbot is still available where required by official AI-05, including outside the new Intelligence page; avoid a second disconnected conversation store.
7. Shared sidebar collapse must not degrade non-Radar pages or routing. Use a small, safe change, preserving layout.
8. Maintain existing access policy and anti-forgery; expand authorization only as approved for internal roles.
