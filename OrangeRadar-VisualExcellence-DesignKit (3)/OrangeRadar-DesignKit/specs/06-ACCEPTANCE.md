# Acceptance and QA — evidence required

Do not announce “done” based on build only. Use existing test harness and isolated demo; no modifications to real team data. Record baseline and after. Save screenshots outside source tree or designated artifacts folder ignored by Git. Compare visual references critically, not literally. Run functional tests and report measured metrics. If no physical microphone/browser/PDF renderer, report exactly what was not tested.

| ID | Required verification | Pass evidence |
|---|---|---|
| A01 | Correct worktree/branch/startup DB; prior changes safe | git diff/status and verified profile, no destructive commands |
| A02 | Long page six scenes, one main scroll, sticky anchor nav | full-page and 1366×768 captures, no horizontal overflow |
| A03 | All suppliers default and isolated visible; count reconciles | compare authorized DTO/table/graph, demo and normal dataset |
| A04 | Category, competition, tender, forensic semantically distinct | screenshots for all; tests for grouping/closed-round edge rules |
| A05 | No fake central hub/edges; aggregate backed by pair IDs | inspect real graph DTOs, test no-link categories, duplicates, multi-type |
| A06 | Group click zoom/expand/inspector; back restores context | actual pointer/keyboard interaction and before/after capture |
| A07 | Supplier focus and isolated node; expand neighbors | actual drill-down, inspector metrics/record links |
| A08 | Edge click evidence & alternative explanation; path/no path | verify stored records, correct link type and count |
| A09 | Search, filters, clear, zoom/pan/fullscreen/export/table | end-to-end interactions; counts reflect scope |
| A10 | Multi-select and chart cross-highlight | same IDs across network, charts, AI context |
| A11 | Historical replay & briefing cancellation | real date/event sequence, user controls, stale events ignored |
| A12 | Market charts legible and honest sparse/empty states | team & demo screenshots, no awards vs true zero, valid units |
| A13 | All detector/evidence/document functions preserved | tests; 8 detectors, exact vs semantic limitations shown |
| A14 | Voice compact dock and expanded view share session | real state transitions, one mic stream, text fallback |
| A15 | Waveform driven by audio input/output | real/fixture audio and state; idle is not fake talking |
| A16 | AI changes graph/selection/section through safe tools | Arabic/English demo, trace real tool calls, invalid IDs rejected |
| A17 | Stop/end/scope change cancels stale work and mic released | automated fixture and physical mic if available; barge-in labeled if unverified |
| A18 | Report & scenario work with selection, no official mutation | generate PDF and open if possible; isolate what-if results |
| A19 | Responsive 1920×1080, 1366×768, 1024, 390; dark/light | screenshots per viewport, labels visible, drawer accessible |
| A20 | Accessibility & motion | keyboard nav, focus, contrast, reduced-motion, no scroll trap |
| A21 | Authorization/sealed bid regression | unauthorized/anonymous/CSRF checks; never leak sealed records |
| A22 | Existing 54 Radar checks and 7 Sourcing Officer pages | actual rerun counts, failures disclosed; existing UI unaffected |
| A23 | Performance | measure graph interaction, no console errors, compare voice latencies; no fake scores |

## Screenshot checklist
Use 1366 and 1920 widths with real demo data: full page, all-market default, competition, tender, forensic, selected category, focused supplier, selected edge/evidence, path, historical replay, market charts, full voice workspace and compact dock, report/scenario. Also light theme, phone layout and sparse normal dataset. Capture at meaningful state completion, not arbitrary timeout. Record file names and scope. If possible short screen recording of actual voice-to-network transitions. Review visual defects and iterate. Target 0 new clipping, overlapping names when default small dataset permits, unreadable legends or unnecessary glow. Check no console errors or failed app requests, excluding clearly explained environmental noise.

## Handoff
Report baseline vs final: files, libraries, actual git branch, route, tests and exact commands, screenshots, remaining limits (PDF/OCR/Arabic export, physical barge-in if not tested), performance measurements, any decisions or blocked tests. Do not claim 100% if tests missing. No automatic commit/push.
