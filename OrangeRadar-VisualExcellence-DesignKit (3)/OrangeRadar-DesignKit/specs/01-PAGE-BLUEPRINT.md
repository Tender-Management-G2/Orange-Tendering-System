# Page architecture — six scenes, one workspace

## 1. Command Deck
Use existing app chrome, not replacement global navigation. Compact title + subtitle, scope/date controls, refresh, Brief Me, Start Live Conversation, Export. 4–6 primary KPIs with secondary metrics accessible (all old data available). Show current filter scope and demo marker where relevant. No decorative globe implying global market data. On 1366×768 show graph heading/start of canvas in first viewport without excessive hero height. Sticky compact anchors: Overview / Network / Market / Forensics / Analyst / Reports. Active anchor responds to scroll; no mandatory sequential navigation.

## 2. Market Network Canvas
Primary experience full available width with comfortable 540–760px desktop canvas roughly adjusted to viewport; clamp appropriately on small displays. Toolbar above canvas, compact floating legend and zoom. Inspector closed by default; open a 300–360px right drawer only for selection or evidence, resizes canvas without hiding labels; on narrow screens use a collapsible inline panel or accessible dialog. No permanent huge side rails. Graph must remain the visual star. See graph spec.

## 3. Market Patterns
Prefer staggered full-width featured visualization plus secondary companion chart, then deeper analysis. Competition and price views should have ample width and clear units. Use connected filters and selection from network; show active supplier chips and reset. Supplier landscape with award-based axes only if meaningful denominator available; otherwise offer participation vs invitation response with explicitly changed labels. Award distribution compact empty state if no awards; keep tables and export. Supplier DNA: readable axes, normalization definitions, missing values as missing, compare up to a small number of selected suppliers, details in expandable content not tiny inner scroller. Preserve competition by requesting department if tender has no category. Charts have table alternatives and keyboard selection.

## 4. Forensic Lab
Show review signals and context distinction; choose finding from list; evidence inspector with supporting record links, timestamps, calculation, alternative explanation, sample and limits. Place large relationship evidence visualization next to selected finding when room; otherwise stack. Document DNA exact/hash vs semantic clearly distinct; side-by-side passages if available, missing extraction stated. Timeline scrubber with play/pause/speed, actual dated events, no future leakage. Existing 8 detectors preserved. No risk/confidence as decoration.

## 5. AI Conversation Workspace
Full-width dedicated experience: audio-reactive surface and voice controls at left/top, transcript with readable Arabic/English and evidence action cards, contextual suggested prompts. A compact dock pinned to safe bottom/side edge across sections lets user start/stop/mute/open, shows state and last action; not a second independent session. Collapsible expanded panel only on demand, no overlay of key controls. Text input and keyboard support. See voice spec.

## 6. Reporting & Scenario Studio
Preserve real report export, what-if simulation, brief. Show selected evidence pins, scoped summary, report options actually supported, and before/after comparison as hypothetical. Read-only simulation cannot change supplier availability. Maintain chosen filters and selections across views. Save non-sensitive display preferences if useful; persistent investigation history is separate if server storage needed. Never save raw audio or sensitive records in browser storage.

## Responsive topology
1920: generous canvas, graph mostly visible; 1366: no clipped toolbar, inspector optional, user can use full screen; 1024: compact filters, inspector stacks; 390: single-column, graph/table toggle, touch pan/zoom graph only when interacting, no body horizontal overflow. Use main document scrolling, no nested scrolling except bounded transcript/dialog when genuinely needed. Use scroll-margin for sticky chrome; avoid sticky overflows and focus traps. Respect existing Arabic direction strategy (dir=auto for chat; RTL layout support where UI localized).
