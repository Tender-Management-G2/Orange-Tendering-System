# ORANGE RADAR — Supplier Market Intelligence (Orange Intelligence)

Route: **`/Intelligence`** (sidebar: **Orange Intelligence**), `Controllers/IntelligenceController.cs`, view `Views/Intelligence/Index.cshtml`.
The old `/SourcingOfficer/MarketIntelligence` address redirects there.

## Orange Intelligence V3 (2026-11-21)

- **Access:** every internal role except Supplier (Admin, Sourcing Officer/Manager, Requesting Department, Department
  Representative, Technical, Finance, Evaluation Committee, top executives). **Data scope** is enforced in
  `RadarDataService.ScopeAsync`: procurement, management, executives and Admin see the whole market; every other role sees only
  the tenders of their department and the tenders whose committee they sit on, and only the suppliers invited to them.
  The snapshot cache key includes the scope, so the charts, exports and the AI Studio all read the same scoped data.
- **Four exclusive tabs** (one shown at a time, `#overview|suppliers|tenders|studio`): Overview (scope KPIs, lifecycle
  funnel, items worth attention, shortcuts) · Suppliers (4 KPIs, market snapshot, the 3D **Supplier Universe**
  `wwwroot/js/orange-intelligence-universe.js`, inspector on selection, 2D network and table fallbacks, supplier insight charts,
  offer comparison, what-if scenario) · Tenders (any tender in scope: lifecycle from recorded timestamps, offers of closed
  rounds, Excel / PDF export) · AI Studio (Gemini: supplier summary, text chat, live voice call — see below).

"The market" means the suppliers and procurement activity recorded in this system, never the external supplier market.

## Architecture (no new tables, no migrations)

```
Services/Radar/
  RadarOptions.cs          configuration (thresholds, demo dataset, cache)
  RadarSnapshot.cs         read-only snapshot of the period + sealing rule + scope, 60 s memory cache
  RadarAnalytics.cs        market metrics, landscape, competition, awards, prices, simulator
  RadarForensics.cs        behavior patterns (pure LINQ, stable ids)
  RadarGraph.cs            graph nodes/edges + network projections
  RadarService.cs          facade used by the page and the AI Studio
  RadarExportService.cs    Excel / Word / PDF exports
  IntelligenceAiService.cs AI Studio: supplier facts, Gemini text request, Live token
  RadarDemoSeeder.cs       fictional demonstration market (isolated database only)
wwwroot/js/orange-radar.js (page state, tabs, charts), orange-radar-network.js (2D network),
  orange-intelligence-universe.js (3D universe), intelligence-ai.js (summary + chat), intelligence-audio.js (live voice)
wwwroot/css/orange-radar.css, orange-intelligence.css
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
| 8 | Submission-time proximity | Latest revisions submitted ≤ 10 min apart in ≥ 3 shared rounds. | signal |


## AI Studio (Google Gemini)

Three features, one system prompt (`IntelligenceAiService.SystemPrompt`). Gemini never reads the database: the server reads a
few non-sensitive facts of one supplier (name, category, status, invitations, participations, awards, response and win rate,
first/last activity) from `RadarAnalytics.Suppliers` and puts them in the prompt. A supplier outside the user's scope is rejected
(404) before anything is sent.

* **Summary / Text chat:** `POST /Intelligence/SupplierSummary` and `/AskSupplier` → one `generateContent` request
  (`Gemini:TextModel`, default `gemini-3.5-flash-lite`) → `{ answer }`. Each question is independent (no history).
* **Live voice call:** `POST /Intelligence/StartVoice` returns a single-use ephemeral token (`v1beta/auth_tokens`, 1 minute to
  start, 10 minutes to finish). The model, prompt and supplier facts are locked inside the token. The browser connects to
  Gemini Live (`Gemini:LiveModel`, default `gemini-3.8-live`) with `@google/genai` and streams 16 kHz PCM microphone audio;
  Gemini answers with 24 kHz PCM audio. Gemini's own voice activity detection handles turns and interruptions.
* **Key:** User Secrets `Gemini:Orange-Intelligence-API`, read on the server only; the browser only ever receives the
  ephemeral token. Both models are on the Gemini Free Tier.

## Demonstration dataset

Launch profile **`https (Orange Radar demo database)`** uses database `OrangeTenderingTeamDev_RadarDemo` and sets `OrangeRadar:DemoDataset:Enabled=true`. The seeder refuses any database whose name does not end with `_RadarDemo`, so `OrangeTenderingTeamDev` is never touched. It creates 12 fictional "RADAR DEMO" suppliers and 10 tenders containing: normal competition, repeated participation, a shared phone/address pair, a stable 4 % price gap, list-price twins, an identical file, legitimately similar (template) text, a re-worded methodology, award rotation, low-competition rounds, a single-bidder tender, a pending and a suspended supplier, and a sealed open round. The patterns are planted as data; the real detectors find them.

## Network rules

* Category membership is a registered classification. A line between suppliers exists only for a detector finding or (competition view) shared closed rounds; nothing is aggregated into an invented category link and there is no hub node. Group "links to other categories" counts distinct real supplier pairs.
* Competition: a pair is linked when both submitted offers in at least N distinct closed rounds (`OrangeRadar:Thresholds:CompetitionLinkMinRounds`, default 2, adjustable in Filters); each round counts once. It is ordinary market context, never a signal.
* Node colour = registered category, shape = entity (supplier circle, closed round square, shared contact hexagon, document diamond), size = closed rounds with an offer (34-64 px, square-root scale). Hollow = no closed-round offer; dashed outline = not approved. Grey = context, amber dashed = review signal, violet dashed = semantic document similarity, orange = selection / path.
* Layouts are deterministic presets (no physics). One motion coordinator cancels any running transition before the next; `prefers-reduced-motion` makes every change instant.
* The replay shows only what was available at the cursor date (offers when their round closed, patterns when established).
* Only the selected network view is stored in browser storage; no names, ids, findings or conversation.

## Limitations

* Live voice uses the browser's echo cancellation; with loudspeakers Gemini may hear itself, so headphones are recommended.
* The Gemini Free Tier has rate limits and models can be temporarily busy (503); the page then shows a clear message.
* Thresholds are demonstration defaults for a small academic dataset.
