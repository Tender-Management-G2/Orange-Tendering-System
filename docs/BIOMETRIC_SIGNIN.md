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
| "Use my phone" (QR code / email link) | `Controllers/PhonePasskeyController.cs`, `Services/Biometric/PhoneLink.cs`, `Views/PhonePasskey/Index.cshtml` (phone page), `Views/Shared/_PhonePasskeySetup.cshtml`, `wwwroot/js/phone-passkey.js`, `wwwroot/css/phone-passkey.css` |
| Tests | `Tests/AuthSmoke/BiometricChecks.cs`, `Tests/AuthSmoke/PhonePasskeyChecks.cs` (`AuthSmoke.dll --phone-checks` or `--biometric-checks`) |

## Adaptations made to the supplied code

- Namespaces, `ApplicationUser`, supplier rule (`SupplierStatus.Approved`), `MustChangePassword` and role-workspace redirect now match this project.
- Face templates are a normal EF entity + migration instead of runtime-created DDL in a `biometric` schema.
- Audit rows use the project's `AuditLogs` columns (no IP column); no biometric data is ever written to it.
- **Current flow — the supplier chooses the method first** (owner decision 2026-11-16, replaces the two bullets below). The
  Login page shows Supplier / Other User, and for a supplier three methods: **Email & Password**, **Face ID**, **Fingerprint**.
  Other users always use email + password. Email & Password (`handler=Credentials`) signs an approved supplier in directly with
  the password-step session (`Biometric:SupplierPasswordOnly`). Face ID and Fingerprint ask only for the email:
  `handler=IdentityStart` (method `face` / `fingerprint`) opens the `SupplierPreAuth` step only for an approved, active supplier
  who has that method set up; any other email gets the same "not available" answer (409) and no step (rate-limited per IP).
  Face ID then runs `/Biometrics/FaceChallenge` + `FaceLogin`; Fingerprint runs `PasskeyLoginOptions` + `PasskeyLogin`
  (this computer's fingerprint reader / Windows Hello first, a phone through the site's QR code as fallback). The old
  "continue with password" handler (`PasswordOnly`) was removed: with the step now opened by an email, it would have been a
  bypass. The separate "Sign in with a device passkey" button and the "passkey" wording were removed from the page.
  **Risk the owner accepted:** Face ID no longer has a password in front of it and there is still no liveness check, so a good
  photo of an enrolled supplier plus their email can pass it. Covered by `BiometricChecks.cs` and `PhonePasskeyChecks.cs`.
- **Employees too** (owner decision 2026-11-17). When an admin-created employee sets the password from the emailed link
  (`ActivateEmployee`), they are signed in and taken to `Areas/Identity/Pages/Account/EmployeeIdentity` to optionally set
  up **Face ID** or **Fingerprint** (the same enrollment endpoints and `_PhonePasskeySetup` as suppliers, password
  re-checked), or **Skip for now** to their workspace. They can return there or use the account menu later. On the Login
  page, **Other User** now also offers Email & Password / Face ID / Fingerprint: `handler=IdentityStart` opens the step for
  any active account with that method set up (`AccountAllowedAsync` still requires an Approved supplier company). The
  Supplier / Other User choice stays a preference; the real role picks the workspace. Covered by `Tests/AuthSmoke/Program.cs`
  (activation) and `BiometricChecks.cs` (employee Face ID sign-in).
- *(Superseded 2026-11-16)* **Supplier face sign-in is password first** (owner decision 2026-11-15). The Login page checks email + password
  (`Login?handler=Credentials`); for an approved supplier with a face enrolled it does not sign in but opens a short pre-auth
  transaction (`Services/Biometric/SupplierPreAuth.cs`: a random HttpOnly cookie pointing at a server-side record,
  `Biometric:PreAuthSeconds`, default 300). `/Biometrics/FaceChallenge` and `/Biometrics/FaceLogin` work only inside it, for that account;
  an email named by the browser is ignored, so a face alone can never start a sign-in. The transaction ends on success, cancel,
  lockout, expiry or when the Login page is opened again. There is **no liveness or anti-spoofing** (owner decision): a photo can pass
  the face step, which is why the password step is mandatory. Device passkeys (WebAuthn, user verification required, server stores
  only a public key) stay a separate, passwordless method and are not labelled as fingerprint. Fingerprint enrollment/verification is
  shown as Coming Soon only. Covered by `Tests/AuthSmoke/BiometricChecks.cs`.
- *(Superseded the same day)* **Every approved supplier sees the three choices** (2026-11-16): with `Biometric:SupplierPasswordOnly` on (the default), the
  correct password always opens the identity step (Email & Password, Face Recognition, Fingerprint / Phone), also when nothing is
  enrolled yet. Before, a supplier with no face and no passkey was signed in by the password directly and never saw the step. A
  method that is not enrolled is shown disabled as "Not set up yet: sign in, then add it from your account menu" (Face recognition
  sign-in / Fingerprint sign-in). With the switch off and nothing enrolled, the password still leads to setup (Pending Approval).
- **Fingerprint / Phone tile** (identity step, renamed from Fingerprint / Passkey on 2026-11-16): runs the WebAuthn passkey
  ceremony scoped to the account that passed the password (`PasskeyLoginOptions` returns only its credentials; `PasskeyLogin`
  refuses another account's passkey), or the QR-code approval described in "Fingerprint / Phone" below. WebAuthn does not tell
  the server which modality was used, so the UI never claims a fingerprint was verified.
- **Face templates and data protection:** the key ring uses a fixed application name (`Orange-Tendering-System`). Before
  2026-11-15 it was scoped to the folder the app ran from, so a face enrolled from one copy (Visual Studio, a worktree, another
  port) could not be read by another and every face sign-in failed. Older templates are **recovered, not lost**: `FaceService`
  opens the same key ring under the old folder scope (the running content root, sibling checkouts in Development, and
  `Biometric:LegacyKeyScopes`) and re-encrypts each template under the fixed scope on first use. A read-only audit
  (`AuthSmoke --face-key-audit`) found all 45 templates of the shared dev database recoverable. Only a template unreadable
  under every scope produces the re-enrollment message.
- **Face capture and matching:** 7-second still capture (one sample every 0.7 s, larger detector input, minimum face size);
  enrollment keeps 8 samples. The decision averages the live samples into one descriptor, which must be within
  `FaceMatchThreshold` (0.5, unchanged) of an enrolled template, and at least 60% of the single samples must be too.
- **Supplier sign-in choices** (owner decision 2026-11-15): after a correct password an approved supplier with a face or
  passkey enrolled chooses Email & Password, Face Recognition or Fingerprint / Passkey, all inside the same pre-auth
  transaction. "Email & Password" (`Login?handler=PasswordOnly`) signs in with the `supplier-password-step` method; the
  supplier workspace accepts it only while `Biometric:SupplierPasswordOnly` is true (set it to false to make face or
  passkey mandatory again).
- **Account type** (Supplier / Other User) on the sign-in form is a preference. After a correct password the server compares
  it with the real role; a mismatch signs nobody in and opens no identity step.
- **Registration identity step** (Pending Approval): the supplier chooses Face Recognition or Fingerprint / Phone; either one
  completes onboarding. Fingerprint / Phone is described in "Fingerprint / Phone (16 November 2026)" below. There is no device
  name to type any more: the server names each device after its browser ("Android phone · Chrome", "iPhone · Safari").
- Enrolling a fingerprint or face re-checks the current password, so a hijacked session cannot add its own biometrics.

## Configuration (optional, `appsettings` section `Biometric`)

`RpId`, `Origins` (set both on a real domain; they default to the request host, e.g. `localhost`), `FaceMatchThreshold` (0.5),
`FaceLoginRequiresPassword` (true), `MaxFailuresPerWindow` (10), `FailureWindowMinutes` (10).
Phone flows: `PublicOrigin` (the public HTTPS address of this same running app, e.g. the ngrok URL; QR codes and email links
made on `localhost` / plain HTTP point the phone there), `PhoneEnrollSeconds` (600), `PhoneSignInSeconds` (180),
`PhoneLinkEmailsPerWindow` (3), `PhoneLinksPerWindow` (10).
Production needs HTTPS (camera and WebAuthn need a secure context; `localhost` also works). Face recognition downloads face-api.js and its
models from jsDelivr, so it needs internet unless those files are vendored locally. Challenges, phone links and throttles are in
memory (single server): restarting the app ends every open QR code, and the supplier simply makes a new one.

## Fingerprint / Phone (16 November 2026)

What the supplier sees:

- **Registration (Pending Approval) and Biometrics/Fingerprint:** "Show QR code for my phone" (after the password re-check). The
  phone camera opens `/Biometrics/Phone#t=…`, the supplier taps **Register this phone** and confirms with the phone's fingerprint,
  face unlock or screen lock. The computer polls every 2 s and updates by itself. "Send setup link to my email" sends the same
  kind of link to the account's verified address through the configured `IEmailSender` (SMTP); a failure is reported, never
  faked. Waiting, expiry (countdown), cancel, retry and failure states are shown. Opened on a smartphone, the page offers
  **Register this phone** directly (no QR code); a computer with its own authenticator (Windows Hello, Touch ID) also gets
  "Use this computer's fingerprint reader instead".
- **Sign-in (identity step after the password, approved suppliers only):** the **Fingerprint / Phone** tile. On a phone the
  phone's own passkey is used. On a computer the standard WebAuthn cross-device prompt is preferred when the browser reports
  it (`PublicKeyCredential.getClientCapabilities().hybridTransport`); otherwise, or via "Show a QR code from this site", the
  page shows a QR code and a two-digit number. The phone opens it, the supplier types the number, then approves with the
  phone's passkey; only then does the waiting computer receive its Identity cookie.

Security decisions:

- Tokens: 256-bit random, single use, 10 min (setup) / 3 min (sign-in), only their SHA-256 kept in memory, carried in the
  **URL fragment** so they never reach server, proxy or ngrok request logs (ngrok's free interstitial reloads the page and keeps
  the fragment). A new code cancels the previous one; Cancel ends it at once; 3 failed attempts (wrong number or rejected
  WebAuthn answer) end it. Password change, lockout or deactivation (security stamp) ends it.
- Opening a link changes nothing. Setup needs a WebAuthn registration that Identity verifies (challenge, origin, RP ID, user
  verification required) for the link's own account; sign-in needs the computer's number plus a verified assertion of that
  account's passkey (another account's passkey is refused).
- Session binding: the computer that made the code holds a separate random HttpOnly, Secure, SameSite=Strict cookie (hash
  stored); only that browser, inside its own live password step for the same account, can collect the session, once. The phone
  is never signed in. Account rules are checked again at that moment (active, Approved supplier, lockout). Enrollment keeps
  the existing rule: Pending or Approved suppliers only (Rejected / Suspended refused); sign-in needs Approved.
- Rate limits: per-IP failure throttle (existing), new QR codes per account per window, setup emails per account per window.
  Every step is audited (`PhoneLinkStarted`, `PhoneLinkOpened`, `PhoneLinkEmailSent`, `BiometricEnroll` passkey-phone,
  `PhoneSignInApproved`, `PhoneSignInCompleted`, `PhoneSignInDenied`) without tokens.
- Nothing biometric is stored or received: WebAuthn keeps the private key on the phone; the server stores the public key in
  `AspNetUserPasskeys`. The phone may use fingerprint, face unlock or PIN, and the server cannot tell which; the UI never claims
  a fingerprint was verified.
- No database change: phone links are short-lived in-memory state like the existing challenges and pre-auth step.

Hosting and domains (ngrok):

- ngrok terminates HTTPS and forwards `http://localhost:5254`. `Program.cs` applies `X-Forwarded-Proto` / `X-Forwarded-For`
  from the loopback proxy only, so the app sees `https://<ngrok host>` (WebAuthn origin) and the real client IP (throttling).
  `appsettings.Development.json` restricts `AllowedHosts` to localhost and ngrok domains; an unknown Host gets 400.
- A passkey belongs to the host name it was created on (RP ID). Phones registered through `debating-evasion-abreast.ngrok-free.dev`
  hold a passkey for that host. **A new domain (another ngrok URL, real hosting) means registering phones again.**
- The computer may stay on `http://localhost:5254`: with `Biometric:PublicOrigin` set, its QR codes send the phone to the public
  address (the phone does the whole WebAuthn ceremony there), and the localhost page waits for the same in-memory link. On a
  localhost page the sign-in tile goes straight to the QR code, because the browser's own prompt on localhost cannot use a
  passkey made for the public host. Without `PublicOrigin`, phone setup on localhost reports that the public address is needed.

Manual test on a real phone (needs the owner): restart the app from the WorkV2 folder (VS profile `https (existing
OrangeTenderingSystem database)`), keep `ngrok http http://localhost:5254` running, then on the computer open
`https://debating-evasion-abreast.ngrok-free.dev`, sign in as a pending supplier, choose Fingerprint / Phone, enter the password,
Show QR code, scan it with the phone camera, accept ngrok's "Visit Site" page once, tap Register this phone and confirm with the
fingerprint. The computer must switch to "registered" by itself. Then: Admin approves the supplier; sign out; sign in on the
computer through the ngrok address; choose Fingerprint / Phone (the browser's own phone prompt, or "Show a QR code from this
site"), scan, type the number, approve on the phone; the computer must open `/Supplier`. Also try: the same QR code twice
(refused), waiting past the countdown (expired), "This wasn't me" (declined), "Send setup link to my email".

## Hardware / not verifiable without a person

- A real fingerprint reader, Windows Hello or phone passkey: tested with Chromium's virtual authenticator and a software ES256 authenticator, not a physical sensor.
- Phone flows: verified end to end in Chromium (separate desktop and phone contexts with virtual authenticators, real QR decoding,
  real SMTP delivery to a local sink, HTTPS on an ngrok-style host name). Not yet verified on a physical phone through ngrok.
- A real face in front of the laptop camera (blink / open mouth / turn head, and match quality): not testable headlessly. The fake camera test only proves the library loads and the camera opens.
