# From Paper to Signal — isolated G2 Motion Lab

Registration and Login are two chapters of one interactive motion study. This lab does not connect to the ASP.NET application, submit credentials, request a camera, verify a face, or grant access. The G2 visual review is pending.

## Run

```powershell
cd motion-lab
npm ci
npm start
```

Open <http://127.0.0.1:4173/>. The server binds to localhost only. The paired visual comparison is at <http://127.0.0.1:4173/compare.html>.

Both chapters start with an empty ivory illustration field and automatically draw the first line. The form on the right drives later visual stages. The Login **Continue in visual demo** button advances a fictional storyboard only; it never validates credentials. Supplier face study opens an abstract optical well. Employee demo routing skips supplier biometrics. Fingerprint is visibly disabled. The fiber pulse has its own labelled post-auth **demo-only** control in the development inspector.

## Inspect

- Play, pause, reverse, restart, scrub, or replay an individual shot.
- Change supplier category and watch the current kit fold down before the next rises.
- Orbit around the real 3D geometry, reset to the authored camera, toggle wireframe, lighting, material, and shadows.
- Use 0.4× playback or the reduced-motion endpoint toggle. The system reduced-motion preference is honored.

The geometry and fold pivots are editable in `paper.js`, `paper-iris.js`, and `signal.js`. Camera choreography, shot timing, and controls are in `main.js`. Reference bitmaps in `references/` are studies only and are not used as scene backdrops.

## Review evidence

Run `node scripts/capture-unified.mjs`, `node scripts/check-unified.mjs`, and `node scripts/record-unified.mjs` with the server running. `node scripts/review-recordings.mjs` decodes video frames in Edge for motion critique. Results are in `evidence/unified/`. Playwright video finalization on Windows may require unsandboxed execution because its temporary video mover can raise `EPERM` inside a restricted sandbox.

See [SHARED_SYSTEM.md](SHARED_SYSTEM.md) for tokens and movement conventions and [UNIFIED_REVIEW.md](UNIFIED_REVIEW.md) for the camera specification, browser evidence, critique, and production boundary. Earlier G2 iteration evidence remains under `evidence/before/` and `evidence/revision/`.
