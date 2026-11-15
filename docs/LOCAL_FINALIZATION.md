# Local application verification — 13 November 2026

Desktop project: `C:\Users\user\Desktop\Orange-Tendering-System`.
Main was safely fast-forwarded to merged GitHub commit `45c8190a76d9918cb6b58f6825c079aa407c1f93`.
No force push, database reset, migration-history reset or fake application accounts were used.

## Backups and preserved local work

Backup directory: `C:\Users\user\Desktop\Orange-local-finalization-backup-20261113T120713Z`.

- Full workspace/private-document archive, SHA-256 manifest, binary tracked patch, Git status and repository bundle were saved before synchronization. Five locked Visual Studio search-index cache files were skipped; no source or private document was skipped.
- Uncommitted tracked/untracked work remains in Git stash `2c925f4e66194b92f3ac5c11ca87ec7210d75b23`, named `Preserve Desktop pre-finalization work 20261113T120713Z`. It was not blindly reapplied over the newer merged implementation.
- SQL Server COPY_ONLY/CHECKSUM backup: `SqlServer\OrangeTenderingSystem.bak` under the backup directory. SHA-256: `FDCB6E51ABD15DA92FA9EE0C9909124D58E742333B7F2A4C5BC8EAD5E1C2A6AC`. SQL Server VERIFYONLY succeeded, and an actual restore to a separate database succeeded.
- A second valid backup remains in SQL Server's default backup folder as `OrangeTenderingSystem_PreFinalize_20261113T120713Z.bak`. Reading that protected folder directly was denied; a separate backup folder with access for the SQL Server service account was used for the accessible Desktop copy.

## Application database

Server: `localhost`; database: `OrangeTenderingSystem`; Windows authentication and SQL Server EF Core provider.

Before synchronization the database had eight migrations and none of Identity's three authentication-state columns. The migration `20261113091517_AddAuthenticationState` was inspected: it only adds `EmailOtpFailedAttempts`, `EmailOtpSentAt`, and `MustChangePassword`.

The backup was restored as `OrangeAuthCheck_Restored_20261113T120713Z`, upgraded first, and checked with DBCC CHECKDB. Only then was the same missing migration applied to `OrangeTenderingSystem`. All nine migrations are now applied; no historical migration was rewritten or reapplied. The combined EF model has no pending changes. DBCC CHECKDB completed without errors.

The application started successfully and seeded the seven standard Identity roles. Final application counts remain **0 users, 0 suppliers, 0 tenders, 0 bids, 0 active Sourcing Officers**. The user explicitly deferred account creation. No business records were removed or fabricated.

## Fresh verification

- Real .NET 10 Release solution build: **0 warnings, 0 errors**.
- Existing HTTP authentication harness extended to cover all seven dashboards and the single-active-Officer guard: **142 passing checkpoints** on a fresh isolated SQL Server database, `OrangeAuthCheck_LocalFinal_IsolatedFiles_20261113`.
- Covered registration/company documents, OTP expiry/replay/resend/throttling, pending/approved supplier sign-in, employee activation, password recovery/reset, lockout, antiforgery, cookie revocation, all seven authenticated dashboard renders, role redirects, cross-role Admin rejection and anonymous dashboard rejection.
- A single active Officer passes the global management check; two active Officers fail closed; deactivating the duplicate restores the guard. Existing external workflow checks also verify management of tenders created by a historical Officer without rewriting creator history.
- Existing archived workflow verification copy, still outside the workspace and referencing the synchronized Desktop app: **35 passed, 0 failed, 0 skipped**. The removed workflow test project was not recreated in the project or solution.
- Workflow coverage includes weight proposals/assigned-Manager lock, accepted supplier invitations, rich draft offers/private attachments, bidding, re-tender limits/history, explicit Proceed, scheduled scoring, incomplete-evaluation blocking, End Evaluation and deadline closure, weighted ranking/PDF, report hashes/tampering, authorized signatures, revisions/new signatures, Manager approval and 3-of-4 Top approval. Both historical database upgrade paths retain fixture records.
- Tests use real SQL Server, not SQLite or EF InMemory. Workflow tests use substituted external HTTP/email transports; those results do not establish live Zoom or SMTP delivery.
- The existing auth harness previously wrote synthetic PDFs beneath the application's content root. It now uses a unique temporary `OrangeAuthCheck_Files_*` content root. Twelve synthetic files created by earlier checks in this session were moved to the backup's `synthetic-auth-upload-artifacts` folder, with only their isolated test-database metadata updated. Pre-existing private documents were checked against the original archive and were not moved or deleted.
- Actual Kestrel HTTPS startup verified at `https://localhost:7182`. Home/Login/Register/Forgot Password return 200; all seven dashboards redirect anonymous users to Identity Login. A valid localhost HTTPS development certificate exists.
- Live Ollama `/api/chat` connectivity check returned model `gemma3:4b`, `done=true`, and `{"ready":true}`. No tender/personal data was sent in that check. Actual evidence request handling is verified separately with substituted HTTP in the SQL workflow tests.

Detailed authentication/migration logs are in the new backup directory. Workflow results are at `C:\Users\user\Desktop\Orange-Tendering-System-backup-20261113T114117Z\integration-test-project-before-removal\TestResults\local-finalization.trx`.

The local source changes from this finalization are limited to the existing authentication harness, its README and this report. They were not pushed to GitHub. No application authorization was removed or relaxed.

## Configuration still needed

- **Real accounts:** deliberately deferred by the user. There is currently no Admin or Officer to log in with. When provisioning is resumed, configure a real initial Admin securely, then provision real staff and exactly one active Sourcing Officer. Never paste account passwords into chat or commit them.
- **SMTP:** no live settings found. Set `Smtp:Host`, `Smtp:Port`, `Smtp:From`, and authenticated `Smtp:Username`/`Smtp:Password` as required by the provider, using local user secrets/deployment secrets. Live registration OTP, Forgot Password and employee activation delivery remain blocked until verified with that provider.
- **Supplier email invitations:** use the same SMTP configuration; set `ExternalInvitations:Enabled=true` and a correct public HTTPS `ExternalInvitations:PublicBaseUrl`. In-app notifications and the SQL outbox are implemented; real external delivery has not been verified.
- **Zoom:** set `Zoom:AccountId`, `Zoom:ClientId`, `Zoom:ClientSecret`, `Zoom:HostUserId` for the authorized server-to-server app and verify meeting creation with real credentials. Manually supplied HTTPS Zoom links remain supported.
- **Ollama:** localhost `http://localhost:11434` with `gemma3:4b` is working. Keep the service/model available for actual analysis. AI output still requires Officer review and cannot approve/award tenders.
- **Signatures:** current working mode is explicitly electronic acknowledgment. Trusted personal certificate-backed CMS signing has not been verified with real credentials; it is not embedded PAdES signing.
- **Revisions:** undefined correction routes stay policy-blocked until explicitly approved policy is configured. Weights, deadline-locked offers and finalized scores are not silently reopened.
- **Face ID:** still incomplete; the login control is disabled and labeled as not implemented. No biometric or liveness functionality is claimed.

To launch locally after stopping the verification server, run `dotnet run --project Orange-Tendering-System -c Release --launch-profile https` from the Desktop project root. Public pages can be used now; authenticated real-user operations await the deliberately deferred accounts and required live delivery configuration.
