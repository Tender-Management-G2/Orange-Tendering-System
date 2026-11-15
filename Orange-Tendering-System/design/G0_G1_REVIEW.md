# From Paper to Signal — G0 inventory and G1 art review

**Status:** G0 inventory recorded; G1 editable art proposal ready for owner review. G2 motion lab has not started. The kickoff's G1 gate requires visual approval before G2 modeling. Nothing in this packet is a working authentication scene or evidence of biometric verification.

## Reference and source inventory

- The complete supplied ZIP is extracted at `MotionDesignKit/`. Its `design/` and four `.agents/skills/orange-*` skills are also installed at the repository root for project-local use. The root `START_HERE_CODEX.md` is the supplied kickoff copy. No existing skill directory was overwritten.
- `design/references/from-paper-to-signal-concept.png` is a proposed mood board, not a runtime backdrop or approved mesh. `design/references/face-enrollment-approved-concept.png` is an approved *concept* for the face stage, not a functional camera or production asset.
- The user-supplied server-room image shows the desired material and miniature-environment quality. It is a reference for the IT category, with no demonstrated runtime asset permission or geometry source.
- `design/storyboards/registration-keyframes.svg` and `login-keyframes.svg` are editable technical sketches from the kit. `design/storyboards/g1-supplemental-keyframes.svg` adds the missing IT/telecom variants, rooted portrait frame, login idle, method picker, and abstract camera reveal. The SVGs are editable paths, not the final material treatment.
- `design/references/g1-paper-architecture-material.png` and `g1-signal-core-material.png` are new built-in imagegen proposals for paper and hardware materials. Their exact prompts, provenance, and limitations are in `design/references/G1_PROMPTS.md`. They must never become full-scene runtime textures.

## G0 — working baseline

| Item | Observed state |
| --- | --- |
| Branch / revision | `main`, `3bdb0a7b5c0bb45101774a85f350cbd2e8d6ecc1`. |
| Worktree | Already dirty on arrival: modified authentication views, services, JS, and tests, plus untracked Living Dossier files. A `.ahmad-db/` untracked directory also appeared during this audit; it was not touched. No reset, branch switch, or migration was run. The new G1 files remain scoped to design and project skills. |
| Build | `dotnet build --no-restore -v:minimal` succeeded on 14 November 2026 with 0 warnings and 0 errors. This checks compilation only. |
| App architecture | .NET 10 ASP.NET Core MVC and Identity Razor Pages; EF Core SQL Server. `Program.cs` maps MVC areas, default MVC routes, and Razor Pages, registers Identity and biometrics, and applies first-login and supplier-approval gates. In Development startup runs migrations, so the production app was not launched for this design-only gate. |
| Registration | `Areas/Identity/Pages/Account/Register.cshtml(.cs)` collects contact/password, company, one of 11 `SupplierCategories`, registration/tax details, and legal files. The server validates category and PDF/PNG/JPEG files (10 MB each, max seven), creates a Pending supplier and inactive/unconfirmed Identity user, saves documents, and sends an email OTP. |
| Email / pending | `VerifyEmail.cshtml.cs` checks a six-digit, ten-minute OTP, limits failed attempts, then confirms and activates the user and redirects to `PendingApproval`. Pending suppliers can enroll face/passkey after password recheck; company approval remains a separate Admin decision. |
| Login / access | `Login.cshtml.cs` uses Identity password and lockout checks, account-state checks, internal role redirects, and a supplier setup path. Approved suppliers with enrolled face or passkey cannot complete the password route; separate biometric entry points exist. `SupplierApprovalGate` requires Approved status and a biometric session for protected supplier routes. |
| Current presentation | The uncommitted Living Dossier CSS uses a whole-scene dossier PNG and origami-face bitmap; this conflicts with the requested physical-continuity art direction. These files were inspected and left unchanged. |

The canonical business specification is `../../TenderingOrangeSystem.md`. Its version is dated 13 November 2026 and its biometric chapter requires spoof/liveness protection. The current `FaceService.cs` and `BiometricsController.cs` explicitly state that liveness is absent by a later claimed owner decision. This is a real specification/code contradiction to resolve before any production biometric UI claims. The existing separate face/passkey login flow also differs from the brief's proposed email/password then supplier biometric progression. G3 needs an owner-approved security flow and server contract; a motion adapter cannot redefine authentication by itself.

The current code includes enabled WebAuthn passkey/fingerprint-related endpoints and UI, while this design brief requires the new fingerprint *presentation* to stay unavailable for now. G2 will show a disabled visual placeholder only. Production integration must decide how to retain or explicitly change the existing passkey path without silently removing working authentication.

## G1 — proposed keyframes

| Keyframe | Editable reference | Continuity decision |
| --- | --- | --- |
| R00 flat sheet | `storyboards/registration-keyframes.svg` frame 01 | Ivory field, ink score, orange registration square; no scene depth. |
| R01–R03 identity card and sleeve | Registration frame 02 | Card is an attached leaf; hinged sleeve closes over it only on local password validity. |
| R04 paper tilt | Registration frame 03 | The same outline rotates into a three-quarter view, then its edge thickness becomes visible. |
| R05 raised company | Registration frame 04 | Wall and roof leaves rotate at base creases; roof and signage follow the walls. |
| R06 IT and telecom | `storyboards/g1-supplemental-keyframes.svg` top left/middle | Same base silhouette and orange square; equipment leaves or tower/cabinet rise from different anchored creases. Other nine category silhouettes remain planned assets. |
| R08 face frame | Supplemental top right, registration frame 06, approved face concept | A portrait leaf rises from the dossier pocket and folds into facets. Camera replaces the abstract preview only when a stream is ready. |
| L00 planar telecom | `storyboards/login-keyframes.svg` frame 01 and supplemental bottom left | One SIM outline, precise pads, square core, and a single curved fiber path. |
| L03 supplier methods | Supplemental bottom middle, login frame 04 | The same core opens its aperture after server role/credential decision; Face is outside-canvas UI, Fingerprint is disabled and labeled Coming soon. |
| L04 camera reveal | Supplemental bottom right, login frame 05 | Aperture remains in place; an abstract lab-only preview occupies the camera plane. No verified state is shown. |

These drawings set topology, silhouette, palette, and scene relationships. The supplied generated mood board and new material studies set a material-quality target, but their still-image perspective and prop spacing cannot be copied directly into animation. The portrait concept likewise contains a success badge and a photographed person; neither belongs in an unverified lab keyframe.

## G2 asset and motion approach, pending G1 approval

1. Author a common scene-coordinate specification (`sheetRoot`, `registrationMark`, `cardHinge`, `sleeveHinge`, `wallFrontHinge`, `wallSideHinge`, `roofHinge`, `identityPocket`; and `coreBase`, `contactPlate`, `apertureBlade[0..5]`, `fiberPath`, `cameraPlane`). Keep editable SVG outline paths and their source geometry. Use a single orthographic Three.js scene per journey starting with planar geometry, avoiding a renderer swap at R04.
2. Build real paper-thickness meshes and crease-aligned pivot groups. Rotate walls/roof from flat rest; preserve the sheet, square marker, camera, and foundation through category changes. Procedural Three.js geometry source is acceptable because Blender was not found on PATH. No GLB placeholders or raster scene textures.
3. Build the login as one connected hardware assembly: engraved SIM pads and circuit traces, a bounded cable tube, a six-blade aperture, and a camera plane behind it. In the lab, the plane holds a clearly marked abstract preview. No camera capture, OTP, credentials, or success simulation.
4. Drive both scenes through a seekable timeline and a sanitized scene adapter (`setPhase`, `setFormState`, `setCategory`, `setCameraState`, `setVerificationState`, `dispose`). Lab controls will use fictional states, with replay, reverse, category switching, orbit, and wireframe. Production binding comes only at G3 after a second approval.
5. For G2 evidence, capture actual browser frames at 0/25/50/75/100%, desktop 1440×900 and mobile 390×844, plus real-speed animation and orbit video. Measure available-device performance, report device/browser, inspect transitions and interruption states, and distinguish measured from target numbers.

[Three.js orthographic camera](https://threejs.org/docs/pages/OrthographicCamera.html) and [scene graph groups](https://threejs.org/manual/pages/scenegraph.html) support the shared geometry and hinge structure. [GSAP timelines](https://gsap.com/docs/v3/GSAP/Timeline/) provide seeking/reversal, while [Playwright video](https://playwright.dev/docs/videos) documents actual browser recording. These are technical references, not tested G2 results.

## G1 visual inspection and remaining decisions

The two supplied PNG concepts, additional server-room reference, and two new material studies were viewed. The registration SVG and new supplemental SVG were rendered in Microsoft Edge and inspected at desktop size; text, silhouettes, and the disabled method state were legible. The login SVG source was inspected, but a second local-file browser route was blocked by browser URL policy before visual capture; no login render is claimed. Headless Chrome also failed to create a local screenshot because its graphics process exited. G2 browser capture capability must be validated after art approval, before any visual quality claim.

**Gate status:** G0 documented; G1 references prepared, awaiting explicit art approval. G2 screenshots, recording, 3D orbit, wireframe, and performance observations do not exist yet. Do not integrate authentication pages or run G3 until a separate explicit approval of G2.
