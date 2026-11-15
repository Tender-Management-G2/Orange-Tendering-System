# Motion choreography and event contract

Times are design targets, not delays applied to security/network operations. Start animations only when their trigger is true; reverse or reconcile to current state when input changes. No animation triggered on every character except coalesced label updates. User may interact while scene animates.

## Registration
| Shot | Trigger | Scene at start -> end | Target duration | Completion evidence |
|---|---|---|---|---|
| R00 | initial | blank canvas -> outlined sheet, scored fold and orange corner | 450ms | true 2D lines, not just screenshot |
| R01 | contact name first valid | a perforation traces the sheet, connected flap slides out and ink reveals name (fictional fixture for QA) | 500–750ms | card shares edge/pivot, remains same object |
| R02 | email locally valid | envelope outline drawn on same card; no verified email icon | 250–400ms | path reveal rather than pop |
| R03 | passwords locally valid/match | card slides into sleeve, hinged flap closes; never expose password | 450–700ms | overlap/occlusion accurate |
| R04 | enter company step | sheet rotates from frontal plane to three-quarter view; geometry thickness and lighting gradually read; walls still flat | 750–1000ms | aligned start/end, no full-scene swap |
| R05 | legal name locally valid | base-connected walls pivot from 0 to 90 degrees; roof unfolds; sign text resolves | 900–1300ms | inspect 25%, 50%, 75% and camera orbit |
| R06 | supply category changes | current modular props retract into base, new panels hinge up; keep base, camera and orange registration marker fixed | 650–1000ms | two distinct reversible category changes |
| R07 | document locally accepted | a legal paper tab travels along page edge, enters pocket; actual server upload recorded only when confirmed | 400–650ms | object enters physically; error doesn't insert |
| R08 | enter face step | identity pocket opens; portrait frame emerges; paper facets fold to face sculpture | 700–1100ms | visible anatomical geometry/pivots |
| R09 | user selects camera / permission granted & stream ready | mask facets unfold to reveal a correctly aligned live video frame; camera failure state retains frame and retry | 450–800ms | live device capture, no static image fallback accepted |
| R10 | provider confirms actual enrollment (or verified provisional enrollment bound to protected onboarding transaction) | camera fades into authorized abstract portrait token; card closes around it and slides into pocket | 650–900ms | no success before actual response; stop camera |
| R11 | actual submission succeeded | props flatten in reverse, pages align and dossier closes, status reads submitted / email verification required; email verified -> Pending Admin Approval | 750–1100ms | statuses match server, no auto approval |

Fingerprint remains optional visual placeholder only; no capture or authentication, no fake success. Clarify if face enrollment is deferred until account creation and secure email verification; existing backend determines final binding sequence. If enrollment service absent, label capture-only experiment and stop before verified state.

### Geometry continuity
For R04, use one genuine plane mesh matching the SVG outline; match SVG viewBox to the projected vertices of a preloaded Three.js plane. Handoff only when both projected silhouettes match; no cut to unrelated artwork. An alternative is one orthographic WebGL scene that starts flat and looks 2D; prefer this if cross-renderer alignment is unreliable. Keep sheet root id and origin consistent. For R05 use crease-aligned pivot groups, child mesh offsets, and actual rotations; do not scale wall height from nothing. Export named pivot/clip data or procedurally author it. Keep paper props collision-free. For R06 keep building foundation and replace/modularly fold category parts around shared anchors. All 11 categories remain valid; 3 hero categories and 8 lightweight distinct foldable kits are acceptable initial asset scope, with category-specific labels and silhouettes.

## Login
| Shot | Trigger | Start -> end | Target |
|---|---|---|---|
| L00 | idle | black technical line drawing -> SIM-like contact plate and one curved fiber cable | 500ms |
| L01 | email locally valid | fine contact traces extend toward module | 250–400ms |
| L02 | password entered | contact pads align; core stays sealed | 350–500ms |
| L03 | server credential result | failure: quiet reset / input error; success internal: direct role redirect; supplier: module gains depth, mechanical aperture opens to method selector | 600–900ms |
| L04 | supplier chooses configured face | optical aperture segments expand; browser camera permission requested by user action; live feed revealed only when stream ready | 450–800ms |
| L05 | face verification pending | restrained signal traveling along short cable segment; no verified indicators | actual processing time |
| L06 | true server-confirmed authentication | core assembles, single pulse runs along fiber, UI transitions to authorized role destination (approved workspace or permitted pending status) | 600–1000ms |
| L07 | failure/cancel | retain sealed core, allow retry per security policy, release camera | 250–400ms |

Fingerprint option disabled with Coming soon, not selectable for authentication. No biometric choices before server determines Supplier role after accepted credentials. No final authenticated supplier session before all required checks. No PII / camera frames / credentials in traces, screenshots or telemetry.

## Runtime contract
Scene adapter exposes `setPhase`, `setFormState`, `setCategory`, `setCameraState`, `setVerificationState`, `dispose`. UI passes sanitized state, not credentials. Authentication backend remains authoritative. Design lab has explicit fictional demo controls, no auth API or success claims. Production adapter only uses real responses; no demo flags in production.
