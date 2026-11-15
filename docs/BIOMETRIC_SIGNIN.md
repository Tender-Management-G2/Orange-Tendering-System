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
- **Face sign-in requires the account password** (`Biometric:FaceLoginRequiresPassword`, default `true`). The face descriptor and liveness
  measurements are computed in the browser, so a forged request carrying a descriptor computed from someone's photo could otherwise sign in.
  Fingerprint (WebAuthn, user verification required, server stores only a public key) stays passwordless.
- Enrolling a fingerprint or face re-checks the current password, so a hijacked session cannot add its own biometrics.

## Configuration (optional, `appsettings` section `Biometric`)

`RpId`, `Origins` (set both on a real domain; they default to the request host, e.g. `localhost`), `FaceMatchThreshold` (0.5),
`FaceLoginRequiresPassword` (true), `MaxFailuresPerWindow` (10), `FailureWindowMinutes` (10).
Production needs HTTPS (camera and WebAuthn need a secure context; `localhost` also works). Face recognition downloads face-api.js and its
models from jsDelivr, so it needs internet unless those files are vendored locally. Challenges and throttles are in memory (single server).

## Hardware / not verifiable without a person

- A real fingerprint reader, Windows Hello or phone passkey: tested with Chromium's virtual authenticator and a software ES256 authenticator, not a physical sensor.
- A real face in front of the laptop camera (blink / open mouth / turn head, and match quality): not testable headlessly. The fake camera test only proves the library loads and the camera opens.
