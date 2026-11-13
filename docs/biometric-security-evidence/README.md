# Biometric security verification

> **Current state (2026-11-13, owner decision): instant face capture, NO liveness.** Blink / mouth / head-turn steps and
> `LivenessVerifier` were removed from registration, login and `/face-demo`. The browser captures 3 descriptors from
> consecutive frames; the server keeps a one-time token per attempt (no replay), checks the samples are valid and from
> one face, and matches with `FaceService.Match` (threshold 0.5). **No liveness or spoof protection is provided: a photo
> or video of an enrolled person can pass.** Supplier approval rules and the password-bypass block are unchanged.
> The notes below describe earlier states.

> **Superseded (2026-11-13, owner decision):** face recognition is now enabled for suppliers as a classroom-grade demo.
> A verified pending supplier registers a face on Pending Approval (password re-checked, server-issued random liveness
> steps, 128-number face-api.js descriptors encrypted in `FaceTemplates`). Approved suppliers sign in with email + live
> face; once a face/passkey is enrolled the password form refuses them. Passkey (fingerprint) is an optional demo.
> Limitation: liveness and descriptors are computed in the browser, so this stops photos and replayed requests but is not
> production-grade spoof protection. Covered by `Tests/AuthSmoke/BiometricChecks.cs` (synthetic evidence; camera behaviour
> needs manual verification). `browser_checks.py` below still asserts the old disabled state and is out of date.
> Registration now shows steps (details → verify email → face registration → pending approval). A Development-only
> test bench at `/face-demo` (Controllers/FaceDemoController.cs) registers/verifies/resets a test face in server memory
> with the same liveness and `FaceService.Match` logic and shows distances; it returns 404 outside Development and never
> creates an account, a FaceTemplates row or a sign-in cookie.
> The text below describes the earlier state.

Face authentication and enrollment fail closed server-side (501), regardless of legacy configuration. Browser face descriptors/liveness flags cannot produce an Identity cookie. Existing encrypted templates and camera code are preserved. Passkey signatures, origin/RP/challenge/UV checks remain enabled. Challenge consumption is atomic, credential counter changes serialized in SQL, and account restrictions prevent MFA bypass. Local jQuery loads before validation scripts.

Actual browser results: 21 PASS, 0 FAIL, 2 BLOCKED (physical fingerprint / camera security). Authentication harness: 211 PASS. Debug and Release builds passed with zero warnings/errors. Applied biometric migration and SQL integrity were verified without record deletion. Supplementary tender regression could not build due to a locked DLL and is not claimed as passing.

Open report.html for screenshots. Backups, logs and isolated source: C:/Users/user/.codex/biometric-security-20261113. Isolated test database: OrangeAuthCheck_BrowserE2E_BioSecurity20261113. Production database was not migrated or seeded by this work. Existing biometric migration was already applied.

Concurrent UI work changed the Desktop branch during execution. Security source remains in the separate codex/secure-biometrics-20261113 worktree and is not merged/pushed. Current restricted permissions prevent final Git metadata writes outside the workspace; no commit success is claimed. Review/integrate the isolated source before using the redesigned Desktop app.
