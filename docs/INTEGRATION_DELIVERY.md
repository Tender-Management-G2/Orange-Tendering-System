# Orange workflow integration

Integration branch: `codex/integrate-orange-workflow`, based on GitHub main
`986068976add8ef61695620ea12f604d262f9281` (2026-11-13).

## Preserved

- Main already merges `Ahmad` (`02fd872`) and `saja` (`99d6721`). Neither branch was cherry-picked again. All branch heads were rechecked before integration.
- Identity OTP supplier registration, first-login password change, activation, password recovery, lockout, cookie revocation and employee provisioning remain unchanged from main. Existing role authorization is retained.
- Saja's supplier profile, invitations, rich draft offers, private attachments, notifications, clarification and result pages remain available.
- Local tender requirements, four-member weight proposals and Manager lock, one global Officer, four bidding rounds, compliance, Ollama, Zoom scheduling, independent scoring, automatic closure/calculations/PDF, committee signatures, Manager/Top approvals, controlled revisions and audit/history are included.
- Exactly seven independent role layouts live under their respective Areas. Supplier layout includes main's sidebar and notification bell. Role-specific business controllers remain in Areas; root controllers provide shared transport/navigation only.
- All nine historical migrations and their designer files are preserved without source changes. The combined snapshot includes the workflow schema and Identity's `MustChangePassword`, `EmailOtpSentAt`, `EmailOtpFailedAttempts`. No new migration was necessary.

## Integration corrections

- One public registration implementation: main's OTP registration. Local administrative review is retained as `SupplierReviewService`; duplicate registration and email-sender implementations were removed.
- Invitation delivery uses the same `Smtp:*` credentials as Identity. `ExternalInvitations` now holds only enablement, public HTTPS origin and retry settings. No credentials are committed.
- Private download/extraction resolves historical absolute registration paths, `App_Data/SupplierUploads/...` paths and bare tender-document keys, restricted to the three known private storage roots. Existing document rows/files are not rewritten.
- Supplier draft submission enters the workflow's SQL Server tender-row lock and checks ownership, role, approved weights, accepted current invitation, complete items and the deadline. Draft mutations and attachment uploads coordinate with the same tender-row lock. The supplier UI links to the rich draft/attachment editor before final submission.
- An approved contact can be linked using company email or a confirmed active login email. Ambiguous matches grant no invitation.
- Award emits the existing supplier portal's `Tender result` notification contract for participating companies, without disclosing competitors' bids or prices.
- Supplier draft/download timing uses the shared server clock where it intersects workflow deadlines.

## Verified

- .NET SDK 10 Release solution build: **0 warnings, 0 errors**.
- `dotnet ef migrations has-pending-model-changes`: **no pending model changes**.
- Existing HTTP authentication harness: **82 passing checkpoints**, including OTP expiry/replay/resend, rollback on email failure, pending/approved supplier access, employee activation, password reset, lockout, antiforgery, open-redirect protection and cookie revocation.
- SQL Server workflow verification: **35 passed, 0 failed, 0 skipped**. Coverage includes bidding/re-tender limits, scoring/closure, automatic reports, authorization, hash/receipt tampering, watermarking, revisions/new signatures, approval gates, supplier draft attachments and both historical schema upgrade paths with a retained department row. Fresh database creation applies all nine migrations.
- SQL workflow databases use randomly named `OrangeTenderingSystem_Test_*` databases. Authentication used `OrangeAuthCheck_Integration_20261113`. SQLite and EF InMemory were not used. SQL tests use substituted HTTP/email transports rather than claiming live external delivery.
- Historical migration sources were compared with main/local originals; all nine are preserved.

### Requested removal of workflow test project

The user subsequently requested removal of the pictured `Orange-Tendering-System.Tests` project. Its source files and solution entry were removed from both working copies and are **not part of this branch/PR**. Its verification copy/results remain outside the repository in the backup directory. The 35 checks above ran from that external copy against the integrated application. Main's existing `Tests/AuthSmoke` harness remains intact. The removed workflow suite is therefore not available to CI from this branch; restore it only if requested. The copied CI configuration referring to the removed project is also excluded.

## Backup and data boundaries

The original workspace was backed up before integration, including tracked changes, untracked source, ignored files, four workflow migrations and Git history:

`C:\Users\user\Desktop\Orange-Tendering-System-backup-20261113T114117Z`

The backup includes a full workspace archive, binary patch, status, manifest and all-ref Git bundle. Only five locked Visual Studio search-index cache files could not be archived; no source files were skipped. The original workspace remains otherwise separate, apart from the specifically requested removal of its workflow test project.

No migration was applied to the existing `OrangeTenderingSystem` application database during this integration. No application records were deleted or authorization relaxed. The isolated authentication database is retained for inspection. Test cleanup only drops randomly named databases created by those checks.

Before deployment, back up SQL Server and private document storage, inspect the target's migration history/schema, and apply only missing migrations through a reviewed deployment procedure. Keep private upload roots at their existing paths or perform an explicitly planned storage migration; do not reset history, squash migrations, or regenerate the initial schema over an existing database.

## Remaining deployment requirements and limits

- Face ID remains a disabled/scaffolded requirement; there is no working biometric enrollment, liveness verification or login.
- Certificate-backed signing was **not** tested with trusted personal signing credentials. Current behavior is explicitly labeled electronic acknowledgment. The optional detached CMS receipt signs a manifest binding the canonical PDF hash; it is not embedded PAdES/PDF signing.
- Real SMTP delivery, Zoom server-to-server credentials and production Ollama availability require deployment configuration and live verification. The integration tests do not establish those external services work in production.
- Undefined revision routes remain policy-blocked until an explicitly approved, versioned policy is configured. Approved weights, deadline-locked bids and finalized scores are not silently reopened.
- The application database still needs a reviewed migration/deployment step and real authorized accounts, including exactly one active Sourcing Officer. No fake production accounts or tender data were seeded.
- GitHub main must remain unchanged; the PR requires owner approval before merging.

Earlier delivery documents in this repository are historical records. This document states the verified integration scope and current limitations.
