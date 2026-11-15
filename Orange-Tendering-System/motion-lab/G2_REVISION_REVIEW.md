# G2 revision — visual review package

**Status:** Revised Motion Lab for review. The first G2 visual submission was rejected. This revision is not G2 approval, final asset approval, a biometric implementation, or permission for G3 integration.

## What changed in the live experience

- **A / Card and sleeve:** The card stays attached to the sheet, gains ink marks, lifts around its scored edge, and settles while bottom, side, and top leaves fold over it in sequence. The closing object has paper thickness, different face tones, contact shadow, and a small Orange seal. The camera dollies into the card and lowers during the lift.
- **B / Pop-up building:** Four wall pivots and a roof pivot remain attached to the same sheet. The roof waits for wall clearance. The front facade has actual die-cut openings; glass, entry trim, a folding reception desk, roof detailing, a warm lobby source, category leaves, and cross-folded trees support the miniature. The camera descends from the sheet, follows the roof arc, then rests on the entrance and company environment.
- **C / Paper iris:** The rejected polygonal face was removed. A connected circular die-cut leaf rises; eight curved paper blades turn about pinned pivots while a shared Orange ring with visible drive indexes rotates. The opening exposes a recessed abstract optical lens. No camera, identity result, success icon, or biometric data is present. A near-frontal macro shot lets the blade geometry be inspected.
- **Orange Signal:** The rejected polygonal preview was removed. The hardware scene retains the SIM tray, copper contacts, traces, optical aperture, and fiber. A dark optical glass plane with focus rings replaces the face graphic. The camera study is explicitly fictional and does not authorize application access.

## Browser evidence

Run the lab at <http://127.0.0.1:4173>. Play the directed shot buttons in the Development Panel, switch speed to 0.4×, scrub, reverse, orbit, and toggle wireframe. The server must be running (`npm start` from `motion-lab/`).

| Shot | Rejected first pass | Revised intermediate | Revised end | Normal video | Slow video |
|---|---|---|---|---|---|
| A / Card and sleeve | [Before 25%](evidence/before/paper-desktop-025.png) | [Lift 27%](evidence/revision/paper-desktop-270.png) | [Closure 38%](evidence/revision/paper-desktop-380.png) | [A normal](evidence/revision/a-card-normal.webm) | [A slow](evidence/revision/a-card-slow.webm) |
| B / Pop-up building | [Before 75%](evidence/before/paper-desktop-075.png) | [Walls 62%](evidence/revision/paper-desktop-620.png) | [Miniature 76%](evidence/revision/paper-desktop-760.png) | [B normal](evidence/revision/b-building-normal.webm) | [B slow](evidence/revision/b-building-slow.webm) |
| C / Paper iris | [Before 100%](evidence/before/paper-desktop-100.png) | [Blades 95%](evidence/revision/paper-desktop-950.png) | [Optics 100%](evidence/revision/paper-desktop-1000.png) | [C normal](evidence/revision/c-iris-normal.webm) | [C slow](evidence/revision/c-iris-slow.webm) |

The [capture manifest](evidence/revision/manifest.json) records the Edge version and files. [Decoded video frame review](evidence/revision/video-review.json) includes 25%, 50%, and 75% frames from actual playback of all six paper recordings. [Mobile frames](evidence/revision/paper-mobile-1000.png) show iris framing at 390×844. [Wireframe](evidence/revision/paper-wireframe-final.png) and [orbit](evidence/revision/paper-orbit-final.png) captures expose the geometry from other views. [Signal optics](evidence/revision/signal-optics-final.png) and [contacts](evidence/revision/signal-contacts-final.png) have separate revised browser screenshots, plus [normal](evidence/revision/signal-normal.webm) and [slow](evidence/revision/signal-slow.webm) recordings with their own [decoded frame review](evidence/revision/signal-video-review.json).

The [current building view](evidence/revision/paper-current-final.png) and [current iris view](evidence/revision/iris-current-final.png) show the final inspector layout with the authored studio rig. The shot videos were recorded before the extra lighting, clay and shadow comparison controls were added; their scene geometry, camera paths, and default rendering are unchanged.

## Authored camera path

The camera is now a 35° perspective camera. Position, target, up vector, and zoom are keyed independently; the interpolation is smooth and reversible. The principal camera poses are:

| Progress | Camera position (x, y, z) | Target (x, y, z) | Zoom | Intent |
|---|---|---|---:|---|
| 0.18 | −1.75, −0.05, 6.3 | −1.82, 1.57, 0.06 | 2.12 | Identity macro, nearly overhead |
| 0.29 | −2.2, −3.35, 4.7 | −1.82, 1.6, 0.09 | 2.18 | Paper thickness and sleeve parallax |
| 0.58 | 2.7, −6, 5.7 | 0.55, 0.05, 0.28 | 1.05 | Pop-up hinge and foundation |
| 0.73 | 3.1, −5.1, 4.15 | 0.53, 0, 1.1 | 1.0 | Follow the moving roofline |
| 0.82 | 1.95, −5.3, 4.05 | 0.55, −0.03, 0.64 | 1.08 | Company hero rest |
| 1.00 | 3.9, −4.2, 0.65 | 2.60, −0.64, 0.54 | 2.0 | Near-frontal iris macro |

The last iris zoom is reduced slightly on mobile so the die-cut leaf stays inside the 390-pixel canvas. Full camera poses and current sampled values are in `main.js` and `evidence/revision/qa.json`.

## Mechanics and interaction checks

`node scripts/check-revision.mjs` uses a real Edge browser at the local URL. Its [QA record](evidence/revision/qa.json) verifies a planar card at 0%, a lifted card at 23%, closed sleeve hinges at 38%, intermediate wall angles at 62%, roof rest at 82%, iris blade motion at 92–100%, reverse playback, supplier category changes, orbit, wireframe, lighting and material comparisons, shadow toggle, disabled fingerprint choice, reduced-motion endpoint behavior, WebGL fallback, and no page errors. [Raking light](evidence/revision/paper-raking-light-final.png), [neutral clay](evidence/revision/paper-neutral-clay-final.png), and [shadows off](evidence/revision/paper-without-shadows-final.png) are captured. These are implementation checks. The videos and frames are the visual evidence.

## Art critique and remaining limits

The perspective pass gives more depth to the facade and paper thickness than the rejected orthographic pass. The card closure now reads as sequential folding, and the iris has a coherent circular mechanism and optical recess. The intermediate building recording shows the wall, roof, and tree motion in one coordinate system.

The miniature still has stylized, relatively simple secondary category props; it is not yet a film-grade asset across all eleven categories. The paper linkages are directed hinge rigs, not a validated manufacturable pop-up pattern or collision-solved sheet. The iris drive ring and blades share an authored motion parameter; the prototype does not yet include a physical pin-and-slot linkage. A few seam clearances and the tiny top closure deserve further art review at slow speed. The Signal assembly has improved optics but still needs a dedicated premium material and camera pass if its visual bar remains below the reference. Texture and lighting are browser procedural approximations, with no authored physical studio render. These limits should be judged visually from the captures rather than hidden behind performance or geometry claims.

## Safety and approval boundary

This isolated lab uses only synthetic shapes. It never requests camera access or performs enrollment, liveness, verification, or login. Existing uncommitted production authentication work was left untouched. The supplier authentication sequence discrepancy and missing liveness protection remain unresolved in `design/G0_G1_REVIEW.md`. G3 and production integration remain on hold until a new explicit approval gate.
