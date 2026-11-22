# Art direction and motion system

Intent: premium precision, calm intelligence. Like a modern investigative instrument, not a sci-fi poster. High contrast, subtle depth, clean whitespace, disciplined typography. Dark surfaces navy/ink; light surfaces warm white/slate. Orange is brand and active focus, amber review, blue/teal/violet category colors. Do not use red for supplier suspicion by default. Avoid pure black on dark background, neon framing, giant gradients, fine print. Reuse existing brand fonts/icons; use system fallback if no local fonts. Do not download fonts unnecessarily. Tokens offered in starter/radar-tokens.css as adaptable proposal, not a mandatory override. Scoped CSS only; namespaced selectors.

Typography target: page title 32–38px desktop / 26px mobile, section headings 22–26px, body 14–16px, labels 12–13px minimum where possible. Graph labels scale logically (minimum 11–12px at intended zoom); chart axes and legends must not clip. 8px spacing rhythm, section gap 32–48px, 16–24px padding. Use 1px border, limited soft elevation, 12–16px radii; no card nesting. Avoid displaying all data above fold. Data colors consistent across charts/graph; theme-specific contrast validated at WCAG AA for ordinary text and visible focus controls.

Motion choreography (targets, not guarantees):
| Interaction | Motion | Timing |
|---|---|---|
| Section entrance | small translate 8px / opacity once, not delayed content | 180–260ms |
| Network initial reveal | real nodes appear from computed positions, edges fade | 250–450ms stagger max 350ms |
| Change view | animate stable nodes to targets, edge transitions, preserve identity | 350–650ms |
| Select group/supplier | camera fit to padded bounds, dim unrelated, outline active | 300–550ms |
| Inspect edge/path | 2-step endpoint focus and sequential path highlight | 150–250ms per hop, cap ~1.2s |
| Drawer | translate/fade, resize graph at end and during if stable | 200–300ms |
| Chart cross-highlight | update ECharts series by stable id; animate relevant differences | 250–450ms |
| Replay | actual event cadence, adjustable, deterministic scrubbing | user controlled |
| Voice | audio envelope, smooth amplitude, restrained state pulse | real signal, frame budget capped |

Ease-out cubic for UI, smooth ease-in-out camera. No forced scroll except explicit navigation or approved AI action, with cancellation on user input. Use single animation coordinator with cancel token/epoch; no overlapping layout animations or stale scene commands. No animations from fake scan results. Use `prefers-reduced-motion` to skip positional motion, turn off pulses, make state visible instantly. Dynamically stop RAF when offscreen/hidden, idle or disposed. Scope animation to actual action, no perpetual particle effect. Maintain focus, avoid layout shift, tooltip clipping and hidden scrollbars as a crutch. Optimize real browser performance; aim smooth 60fps on owner laptop, measure and report rather than promise.

Cytoscape: batch element updates, reuse instance, style selectors/classes, stable layout targets, cancel animations before new action; use real `cy.resize()` on layout changes. ECharts: reuse instance, stable series IDs, update `setOption`, `ResizeObserver`, dispose on teardown, theme update preserving selection as appropriate. Use actual pointer and key handlers or appropriate HTML alternatives. Visual test both themes; screenshots include no fake contents from concept references.
