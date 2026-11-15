# Face and fingerprint / passkey demonstration

Implemented and checked on 13 November 2026.

## What changed

Face registration, face login and `/face-demo` now capture a single face for at least five seconds, with a visible countdown. Capture restarts when no face or multiple faces are detected. The existing local face-api.js recognition model produces 128-number descriptors; the server requires a five-second span of ordered samples. The matching threshold remains **0.5**. No images are uploaded or stored. This remains the requested academic face-recognition flow without liveness protection; the timer is not anti-spoofing or Apple's Face ID.

The existing Orange UI, biometric controller, password checks, approval checks and audit service were reused. The unfinished migration away from the teammate ZIP's custom WebAuthn verifier is complete: .NET 10 Identity creates and verifies passkey ceremonies and stores credentials in `AspNetUserPasskeys`. The custom verifier/CBOR parser and old credential entity were already staged for deletion when this work began; they remain removed. There is one active passkey implementation.

Supplier onboarding is Company details → Verify email → Face registration → Register Fingerprint / Passkey → Pending approval. Both enrollments must exist before setup is shown as complete. Approval remains separate. Pending suppliers resume by signing in with their original email and password; their account and face templates are retained. After approval, face and passkey work independently. Employee password login and the existing supplier password restrictions remain in place.

`/fingerprint-demo` reuses one inactive, role-free Identity test account for each browser's protected demo cookie. It supports registration, repeated verification and reset. It never issues an application sign-in cookie, and its passkey cannot sign in through the real login endpoint. All demo routes return 404 outside Development.

## Run and URLs

The build verified during this implementation is running at:

- [Fingerprint demo](https://localhost:7191/fingerprint-demo)
- [Face demo](https://localhost:7191/face-demo)
- [Supplier and employee login](https://localhost:7191/Identity/Account/Login)

The existing 7182 and 7190 processes were left running. The existing 7190 instance returned 404 for `/fingerprint-demo`; restart it to load the new implementation. The 7182 instance returned 200 for that route, but was not used to establish this build's test results. Rebuild/restart Visual Studio before using its default 7182 URL. Launch settings were not changed.

To start this version yourself from the web project folder:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --no-launch-profile --urls https://localhost:7191
```

Stop the previous 7191 instance before starting another on that port. Use a trusted ASP.NET development HTTPS certificate (`dotnet dev-certs https --trust` if needed). Keep one browser session on one origin during each ceremony. The RP ID defaults to `localhost` for localhost requests; the response origin must exactly match the request's HTTPS origin. Use `localhost`, not a mixture of localhost, IP addresses and machine names. Configure `Biometric:RpId` and `Biometric:Origins` explicitly for a deployed hostname.

## Laptop and smartphone steps

1. Use an updated Chrome or Edge browser on the laptop. Enable Bluetooth on both laptop and phone, keep them nearby, and give both internet access. Configure the phone's screen lock and fingerprint unlock in advance. A laptop fingerprint reader is not needed.
2. On Android, use a passkey-capable password manager such as Google Password Manager with screen lock configured. On iPhone, enable Passwords/iCloud Keychain and device authentication. A Touch ID iPhone can demonstrate fingerprint; Face ID-only iPhones use face unlock or passcode instead. The website cannot force or identify the verification modality.
3. Open the **fingerprint demo on the laptop**, then click **Register Test Passkey**.
4. In the browser/OS prompt, choose **Use a phone or tablet**, **Use another device**, or **Save on another device**. If a local password-manager dialog appears first, open its other options. The wording and available choices depend on browser and OS support.
5. Scan the browser's QR code with the phone camera and follow the phone's confirmation/unlock instructions. Do not open `localhost` in the phone's browser: that would refer to the phone itself. The QR flow connects the phone authenticator to the laptop's ceremony.
6. Wait for **Fingerprint / Passkey Registered Successfully** on the laptop. This message appears only after server validation and persistence.
7. Click **Verify Fingerprint / Passkey**, select the phone again if prompted, and complete verification. Expect **Authentication Successful**. Repeat without registering again.
8. Cancel a prompt and confirm the page reports cancellation/timeout rather than success. Browsers can use the same error for cancellation, timeout and unavailable credentials, so the UI explains that ambiguity.
9. Click **Reset Demo** to remove the test credentials on the server. Delete retained localhost passkeys separately from the phone/password manager if desired. The test account is reused; clearing the browser's demo cookie starts a separate isolated demo account.

For a real supplier, complete company registration and email verification, capture the face for five seconds, then register the phone passkey. If interrupted, use the password form to resume the pending account. After an Admin approves the company, choose **Login with Fingerprint / Passkey** on the laptop. Verify that the correct supplier dashboard opens; sign out and separately test **Login with Face Recognition**. Pending, rejected and suspended suppliers cannot gain dashboard access through passkeys.

Native phone/QR availability must be confirmed on the actual browser and devices. If no phone option appears, verify browser/OS updates, Bluetooth, screen lock and passkey provider support; the application cannot manufacture a missing native option.

Official references: [Microsoft Identity passkeys](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0), [Chrome passkeys](https://support.google.com/chrome/answer/13168025?hl=en), [Apple cross-device passkeys](https://support.apple.com/en-mt/guide/iphone/iphf538ea8d0/ios).

## Database and validation

Migration `20261113192717_UseIdentityPasskeys` replaces the empty legacy passkey table with Identity's standard table. The existing development table was checked and contained **0** records. The migration refuses to proceed if legacy records exist on another installation, preventing silent deletion; those installations need an explicit preservation/re-enrollment decision. No supplier, procurement or face data was reset or reseeded. The existing phone-number column was preserved.

- `dotnet restore`: succeeded.
- Release app and AuthSmoke build: **0 warnings, 0 errors**.
- SQL Server/HTTP integration harness: **317 PASS checks** on a fresh `OrangeAuthCheck_Passkeys_20261113_04` database. Includes real ES256-signed software-authenticator assertions, valid/invalid registration, forged signatures, wrong origins/challenges, missing user verification, account binding, removal, resumed enrollment, approval states, demo isolation/reset/reuse, employee passwords, face matching and Production 404 responses.
- `node Tests/AuthSmoke/FaceCaptureChecks.js`: **4 checks passed** for duration, 128-value descriptors, reset on missing/multiple faces and timeout. The clock, detector and camera are test doubles.
- EF model drift check: no pending model changes.
- Actual HTTPS requests on 7191: demo, face demo, login, biometric JS and recognition model all returned **200**.
- Browser inspection: fingerprint demo loaded and displayed **Supported (HTTPS)** with the real controls and diagnostics.

The signed software authenticator checks exercise real server cryptographic verification; they are **not physical smartphone tests**. Physical camera recognition, phone QR pairing, fingerprint/PIN prompts, browser cancellation and a complete hardware supplier demonstration still require manual testing.

## Changed files

- `Controllers/BiometricsController.cs`, new `Controllers/FingerprintDemoController.cs`.
- `Services/Biometric/PasskeyService.cs`, `BiometricServiceCollectionExtensions.cs`, `FaceService.cs`.
- `Models/Biometric/BiometricModels.cs`, `Data/ApplicationDbContext.cs`, `Program.cs`.
- `Data/Migrations/20261113192717_UseIdentityPasskeys.cs`, its designer, and `ApplicationDbContextModelSnapshot.cs`.
- `Areas/Identity/Pages/Account/Login.cshtml` and its model; `PendingApproval.cshtml` and its model; `Register.cshtml`; `_RegistrationSteps.cshtml`.
- `Views/Biometrics/Fingerprint.cshtml`, `Views/FaceDemo/Index.cshtml`, new `Views/FingerprintDemo/Index.cshtml`, `wwwroot/js/biometric.js`.
- `Tests/AuthSmoke/BiometricChecks.cs`, `Program.cs`, new `FaceCaptureChecks.js`, and this guide.

No push or commit was made. Pre-existing unrelated work was preserved.
