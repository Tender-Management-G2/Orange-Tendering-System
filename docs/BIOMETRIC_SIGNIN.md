# Biometric sign-in (fingerprint + face) — 13 November 2026

Integrated from the supplied `4_6032710101888933897.zip` package into the existing Identity login. There is still exactly one
authentication system: ASP.NET Core Identity and its cookie. Biometric methods are additional ways to obtain that same cookie.

## Where things live

| Part | Location |
| --- | --- |
| Endpoints + enrollment pages | `Controllers/BiometricsController.cs` (the existing scaffold, now real) |
| Pages | `Views/Biometrics/Fingerprint.cshtml`, `Views/Biometrics/Face.cshtml` (linked from the account menu) |
| Login entry points | `Areas/Identity/Pages/Account/Login.cshtml` (buttons enabled; password form untouched) |
| Browser logic | `wwwroot/js/biometric.js`, `wwwroot/css/biometric.css` |
| Services | `Services/Biometric/` — WebAuthn verifier, passkey service, face service, liveness verifier, sign-in rules, one-time challenges, throttle |
| Models | `Models/Biometric/BiometricModels.cs`; entities `Models/Entities/UserPasskeyCredential.cs`, `FaceTemplate.cs` |
| Database | migration `20261113133104_AddBiometricSignIn` (tables `UserPasskeyCredentials`, `FaceTemplates`; existing `AuditLogs` reused) |
| Registration | `AddBiometrics()` in `Program.cs` |
| Tests | `Tests/AuthSmoke/BiometricChecks.cs` |

## Adaptations made to the supplied code

- Namespaces, `ApplicationUser`, supplier rule (`SupplierStatus.Approved`), `MustChangePassword` and role-workspace redirect now match this project.
- Face templates are a normal EF entity + migration instead of runtime-created DDL in a `biometric` schema.
- Audit rows use the project's `AuditLogs` columns (no IP column); no biometric data is ever written to it.
- **Supplier face sign-in is password first** (owner decision 2026-11-15). The Login page checks email + password
  (`Login?handler=Credentials`); for an approved supplier with a face enrolled it does not sign in but opens a short pre-auth
  transaction (`Services/Biometric/SupplierPreAuth.cs`: a random HttpOnly cookie pointing at a server-side record,
  `Biometric:PreAuthSeconds`, default 300). `/Biometrics/FaceChallenge` and `/Biometrics/FaceLogin` work only inside it, for that account;
  an email named by the browser is ignored, so a face alone can never start a sign-in. The transaction ends on success, cancel,
  lockout, expiry or when the Login page is opened again. There is **no liveness or anti-spoofing** (owner decision): a photo can pass
  the face step, which is why the password step is mandatory. Device passkeys (WebAuthn, user verification required, server stores
  only a public key) stay a separate, passwordless method and are not labelled as fingerprint. Fingerprint enrollment/verification is
  shown as Coming Soon only. Covered by `Tests/AuthSmoke/BiometricChecks.cs`.
- **Fingerprint / Passkey tile** (identity step): runs the WebAuthn passkey ceremony scoped to the account that passed the
  password ( returns only its credentials;  refuses another account's passkey). The device
  unlocks the passkey with its fingerprint reader, Windows Hello face or PIN, or the browser shows a QR code for a phone passkey.
  WebAuthn does not tell the server which modality was used, so the UI never claims a fingerprint was verified.
- **Face templates and data protection:** the key ring uses a fixed application name (). Before
  2026-11-15 it was scoped to the folder the app ran from, so a face enrolled from one copy (Visual Studio, a worktree, another
  port) could not be read by another and every face sign-in failed. Faces enrolled before this change must be enrolled again;
  an unreadable template now produces an explicit re-enrollment message.
- Enrolling a fingerprint or face re-checks the current password, so a hijacked session cannot add its own biometrics.

## Configuration (optional, `appsettings` section `Biometric`)

`RpId`, `Origins` (set both on a real domain; they default to the request host, e.g. `localhost`), `FaceMatchThreshold` (0.5),
`FaceLoginRequiresPassword` (true), `MaxFailuresPerWindow` (10), `FailureWindowMinutes` (10).
Production needs HTTPS (camera and WebAuthn need a secure context; `localhost` also works). Face recognition downloads face-api.js and its
models from jsDelivr, so it needs internet unless those files are vendored locally. Challenges and throttles are in memory (single server).

## Hardware / not verifiable without a person

- A real fingerprint reader, Windows Hello or phone passkey: tested with Chromium's virtual authenticator and a software ES256 authenticator, not a physical sensor.
- A real face in front of the laptop camera (blink / open mouth / turn head, and match quality): not testable headlessly. The fake camera test only proves the library loads and the camera opens.
