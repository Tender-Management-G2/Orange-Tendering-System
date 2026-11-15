# ORANGE — From Paper to Signal

Design and agent handoff kit for the academic ASP.NET Core MVC procurement project. This is a **pre-production design kit**, not implemented authentication, final meshes, or verified animation.

## Install without changing the existing app
1. Extract this archive. Copy the contents (including hidden `.agents/`) into the repository root. Merge, don't overwrite any existing skill directories or AGENTS.md.
2. Keep `design/` outside `wwwroot`. The references are design references, never runtime backdrops.
3. Open Codex at the repository root. Use `/skills` or explicitly mention `$orange-art-direction`, `$orange-paper-genesis`, `$orange-signal-login`, `$orange-motion-qa` as appropriate. Restart if new skills do not appear.
4. Paste `START_HERE_CODEX.md` into Codex. The first phase is a standalone motion lab and evidence review; production auth changes are not authorized by that prompt.
5. Approve each gate before integration. Work in a feature branch; preserve authentication, supplier documents, email/OTP, routes, and server validation. Do not delete old code before identifying its dependencies and preserving a reversible baseline.

## Contents
- Four repository-scoped skills: art direction, registration choreography, login choreography, visual QA.
- `design/ART_DIRECTION.md`, `MOTION_SPEC.md`, `ASSET_MANIFEST.md`, `QA_GATES.md`, `REFERENCE_BRIEFS.md`.
- Two editable technical storyboard SVGs (not production artwork), the existing face-enrollment concept PNG, and a new generated combined art-direction board (`from-paper-to-signal-concept.png`). The new board is a proposal for user review, not final geometry.
- `START_HERE_CODEX.md`: stage-one instruction to build and prove the motion, then stop.

## Status / limitations
No repository files were edited, no final Blender models/GLBs were created, no browser runtime was tested, and no real biometric test was performed. The storyboard diagrams establish composition and continuity, not final visual fidelity. Face camera and server-confirmed enrollment are distinct. Fingerprint is a disabled visual placeholder, not an authentication method. Approved/pending supplier states remain server-authoritative. Do not copy real personal data into design assets, recordings or fixtures.

## Official references
- Skills: https://learn.chatgpt.com/docs/build-skills
- SVG morphing: https://gsap.com/docs/v3/Plugins/MorphSVGPlugin/
- DOM continuity: https://gsap.com/docs/v3/Plugins/Flip/
- Real 3D model loader: https://threejs.org/docs/pages/GLTFLoader.html
- Blender glTF export: https://docs.blender.org/manual/en/latest/addons/scene_gltf2.html
- Browser visual evidence: https://playwright.dev/docs/screenshots
- Browser test recordings: https://playwright.dev/docs/test-use-options
