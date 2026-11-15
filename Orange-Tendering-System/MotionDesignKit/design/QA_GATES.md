# Required gates — no self-certified success

G0 Inventory: record branch, baseline build status, relevant auth views/services/routes, current validation, OTP/email and supplier approval behavior; separate rendering from domain logic. Do not delete old design yet. No force reset, broad cleanup or unrelated changes.

G1 Art approval: show actual editable reference frames for registration idle, identity card, paper tilt, raised building, two categories, face frame, login idle, supplier method picker, camera reveal. User must approve look before production integration. Reference image cannot be a background. Keep PII out of reference assets.

G2 Motion proof: standalone local motion lab (no app authentication, synthetic fixtures only). Implement R00–R06 and L00–L04 as genuine interactive scene; camera can use an explicitly labelled abstract preview in lab only. Render intermediate progress 0/25/50/75/100%, show reverse and category switching. Show 3D orbit proof and wireframe in a developer-only toggle. Capture real viewport screenshots 1440x900 and 390x844. Record short video at real speed with synthetic values. If capture tool unavailable, clearly mark visual review blocked; do not proceed to production. No user approval inferred from passing build.

G3 Functional integration (only after explicit approval of G2): replace old presentation assets selectively. Reuse actual Razor forms, routes, validations, CSRF, identity and approval services. Implement role gating on server. Test positive/negative credential checks, employee path, supplier second stage, denied/unavailable camera, retry, real provider success/failure, enrollment ownership, OTP, pending restrictions, and fingerprint disabled. If provider missing, report pending; no fake verification, no bypass. No real PII in recording or screenshots, and no OTP/password/biometric body in logging or Playwright traces. Use real test device privately for biometric testing, not camera fixture as security evidence.

G4 Quality: inspect actual screenshots and recordings for the criteria below. Measure performance on available devices, report device/browser and metrics; no claim of universal FPS. Test keyboard navigation, reduced motion, WebGL failure, resize, mobile and rapid toggling. Run build and relevant tests. Retain rollback path. Provide changed files and verified versus pending status.

## Hard rejection conditions
- Full-screen stock/generated photo or static dossier with CSS overlays; visible cards appearing from nowhere; building swapping with unrelated image; no genuine 3D geometry after the transition; generic network/login lock replacing telecom story; clipping; detached hinge offsets; unaligned video aperture; unreadable form or blocking motion.
- UI depicts verified email from syntax-only validation or biometric success from camera capture alone. Authenticated supplier before server-authoritative verification. Fingerprint placeholder authenticates. Pending supplier access. Security or functionality removed for demo.
- Claim complete based on build, screenshots alone, or unsupported visual QA. Fake metrics, invented test evidence, or missing required recordings stated as done.

## Reviewer checklist (0–2 each, human verifies)
Continuity, physical fold credibility, 2D opening, 3D model quality, category changes, camera alignment, telecom visual identity, typography/layout, responsive usability, reduced motion. Any zero fails; target 17/20 before integration. This score is an internal design gate, not a substitute for security/functional tests.
