# G2 review package — one visual identity, two motion chapters

Status: **visual review requested**. This is an isolated Motion Lab, not production integration or an authentication implementation.

## Open the experience

- Run `npm start` from `motion-lab/`, then open `http://127.0.0.1:4173/`.
- Compare matched browser renders at `http://127.0.0.1:4173/compare.html`.
- The illustration sits left of the form on desktop and stacks above it on narrow screens. Both chapters start from an empty ivory field and draw a line without input.
- The inspector below the experience supports play, pause, restart, reverse, scrub, shot replay, supplier category, orbit, material, lighting, shadow, wireframe, speed, and reduced motion.

## Camera choreography

Both chapters use a 35° perspective camera, the same warm canvas, key direction, shadow softness, and smoothstep interpolation between authored poses. The camera is always aimed at an explicit scene target. Orbit inspection temporarily overrides the authored path and Reset view returns to it.

| Chapter / progress | Camera intent | Geometric event |
|---|---|
| Paper 0–0.14 | Locked macro on the card crease, front-on | Orange envelope perimeter draws from zero; no sheet is visible at frame zero. |
| Paper 0.14–0.39 | Gentle downward parallax, moderate pullback | The card develops ink and lifts; die-cut leaves extend from their hinge axes and fold over it. |
| Paper 0.39–0.53 | Pull back and descend to see the original sheet | The connected dossier and architectural cut lines enter a shared spatial frame. |
| Paper 0.53–0.83 | Oblique architectural view, eased target on company center | Four walls and reception rise; roof closes after the walls; category pieces fold up on local creases. |
| Paper 0.83–1 | Controlled move toward the attached die-cut iris | Eight paper blades open around fixed pivots; the abstract well stays in the dossier. |
| Signal 0–0.12 | Front-on studio drawing plane | Module outline, contact guide, circular optical route, and cable line draw progressively. |
| Signal 0.12–0.55 | Close study of traces and sliding SIM contact carrier | Device footprint grows from the blueprint; material and partial depth develop on the same assembly. |
| Signal 0.55–0.78 | Oblique orbit from above, holding the module and fiber in frame | Beveled housing gains thickness and casts a soft shadow; the carrier slides on its rails. |
| Signal 0.78–0.94 | Macro move centered on the optical well | Eight curved metal blades swing tangentially from fixed outer pivots; dark abstract lens and reticle become visible. |
| Signal 0.94–1 | Pullback and shift target toward the fiber | A single restrained Orange pulse travels inside five visible strands as a **demo-only post-auth storyboard**. |

The authored position and target values are in `main.js`; all movable parts and draw ranges are in `paper.js`, `paper-iris.js`, and `signal.js`.

## Browser evidence

- `evidence/unified/capture.json`: Edge version, desktop and mobile renders at 0, 7, 12/14, 25/32, 39/55, 65/78, 83/94, and 100 percent as applicable. Forty-nine screenshots include paired stage crops.
- `evidence/unified/side-by-side-reveal.png`: registration and login in the shared visual system at the optical reveal stage. The comparison page offers five matched moments.
- `evidence/unified/interaction-check.json`: local field progression, supplier and employee demo routing, unavailable fingerprint, separate pulse trigger, no external requests, and no page errors.
- `evidence/unified/recordings.json`: six Edge WebM recordings: both complete sequences at normal speed; slow paper fold, architecture, optical iris, and fiber studies.
- `evidence/unified/recording-review.json`: decoded video frame review at intermediate timings, not merely static WebGL snapshots.

## Iteration and critique

The first browser pass showed a cropped sleeve and assembly, so the camera pulled back and the mobile zoom was reduced. Signal initially looked pale; the studio lighting and graphite material were rebalanced. Recorded optical frames then revealed that an extruded housing face sat in front of the lens. Raycast inspection confirmed the depth order; the housing offset was corrected and the final optical motion was recaptured. The metal iris now opens rather than visually sealing the well.

The current work is a coherent, inspectable motion prototype. Its miniature company remains intentionally paper stylized and less detailed than a final premium production asset. The signal blade linkage is synchronized geometry but not a manufacturable linkage simulation. The optical well is abstract; it contains no camera image, face matching, liveness test, or biometric claim. The form uses local syntax and length cues only to drive visuals. An authorized workspace transition is not implemented. These are review limits, not hidden production behavior.

## Production boundary

No production authentication file was edited for this update. Existing unrelated uncommitted work was preserved. The supplier credential → biometric sequence discrepancy and missing liveness protection remain unresolved integration issues. No visual demo state grants access or changes server policy. G3 production integration requires the later approval gate.
