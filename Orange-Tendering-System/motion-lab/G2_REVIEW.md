# G2 Motion Lab review

> Historical first-pass review. The user rejected this visual pass. Its evidence is preserved in `evidence/before/`; the current revision and approval boundary are in `G2_REVISION_REVIEW.md`. Descriptions below refer to the superseded first pass, including the removed polygonal portrait.

## Scope

The lab implements a single, timeline driven Three.js scene for Paper Genesis and a separate single assembly scene for Orange Signal. Paper begins planar; the card and envelope are connected to the sheet, walls and roof turn around named creases, category props fold, and a faceted portrait sculpture emerges from a connected leaf. Signal begins with path drawn technical outlines and a depth compressed assembly; its SIM style contact tray moves on rails, copper routes extend, the assembly gains depth, and six aperture blades fold back behind the bezel. The fiber sheath retains a visible optical filament. The camera moves continuously; shots do not swap rendered backdrops.

The lab covers G2's R00–R06 and L00–L04 motion proof, plus an exploratory R08/R09 abstract portrait preview. R07 and later registration states and L05 and later login states are outside this isolated visual phase. The abstract preview uses no camera stream or biometric data and indicates no enrollment or authentication result.

## Browser evidence

`evidence/manifest.json` lists exact screenshots and records measured browser behavior. Key views include:

- `paper-desktop-000.png` through `paper-desktop-100.png`, and the corresponding five `paper-mobile-*.png` views.
- `signal-desktop-000.png` through `signal-desktop-100.png`, and the corresponding five `signal-mobile-*.png` views.
- `paper-telecom-category.png`, `paper-construction-category.png`, `paper-wireframe.png`, and `paper-orbit.png`.
- `paper-playback.webm`, `signal-playback.webm`, `paper-orbit.webm`, and `paper-categories.webm`.
- `*-review-30.png`, `*-review-60.png`, and `*-review-90.png` extracted by actually playing the WebM recordings in Edge, not by rendering scene progress directly.

The recordings and screenshots use only synthetic art. No real customer data, credentials, OTP, or biometric input appears.

## Design critique

The key continuity proof is the mid fold, not only the final building: the recording shows the same roof and walls rotating through intermediate angles, and orbit/wireframe inspection reveals their hinged geometry. The signal aperture's blades now fold behind the bezel; the SIM tray stays on rails and the visible fiber is aligned to its sheath. The portrait phase forms an angular paper face before the abstract preview is exposed.

The remaining visual limitations are visible: the miniature building and secondary category kits use restrained paper relief rather than a fully detailed production model; the abstract face preview is deliberately stylized and is not a live camera composition; lighting and performance need device specific validation during any later production phase. These are review points for G2 approval, not claims of final asset approval.

## Performance and checks

The final capture ran on Windows 10 in Microsoft Edge 157.0.4341.60 at device scale factor 1. Edge reported ANGLE on Intel UHD Graphics through Direct3D11. The 1440×900 browser viewport gave a 953×650 canvas; two second requestAnimationFrame samples measured 166 FPS for Paper and Signal. The 390×844 viewport gave a 364×385 canvas and measured 166 FPS for each. This machine's display/animation scheduler capped the samples near 165 FPS. These are observations on one available graphics path, not production performance targets or claims about other devices. The local in-app browser was also used to inspect and operate both scenes interactively.

The recorded Paper and Signal playbacks reached 100% with playback stopped. The video review script played each file and captured intermediate decoded frames at 30, 60, and 90 percent of its duration. Exact recorded durations are in `evidence/video-review.json`.

The automated browser run reported no page errors at desktop or mobile sizes. It confirmed the disabled fingerprint control, wireframe toggle, orbit state after drag, reverse movement from 80% to 74.2%, rapid journey switching without a page error, reduced-motion endpoint behavior, and a visible fallback when WebGL is unavailable. The checks and raw render statistics are in `evidence/manifest.json`. Visual approval remains a human review.

## Production boundary

The production authentication pages, endpoints, services, and tests were not changed for G2. Existing uncommitted work was preserved. The supplier authentication sequence mismatch and absent liveness protection remain unresolved production issues documented in `design/G0_G1_REVIEW.md`. G2 does not approve a security policy, simulate secure verification, or authorize production integration. Explicit visual approval is required before G3.
