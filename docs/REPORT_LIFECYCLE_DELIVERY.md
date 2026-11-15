> Historical pre-integration delivery record. For the current branch, configuration, verification and removal of the workflow test project, see [INTEGRATION_DELIVERY.md](INTEGRATION_DELIVERY.md). Statements about earlier application-database migrations describe that earlier work, not this integration.

> Current verified workflow and deployment status: [Final workflow delivery](FINAL_WORKFLOW_DELIVERY.md). Earlier status/configuration statements below are historical and superseded where that delivery describes a change.

# Orange automated report lifecycle

Delivered: 13 November 2026. Scope is the requested report generation, signing, watermarking and controlled revision extension. The seven existing role layouts and unrelated business modules were preserved.

## Database deployment

Applied to the actual **localhost / OrangeTenderingSystem** database:

`20261113092128_AutomatedReportLifecycle`

Only two tables were added:

| Table | Purpose |
|---|---|
| ReportSignature | User/stage/section/version-bound signature receipts, hashes, signing method, optional CMS proof and historical invalidation metadata. |
| ReportRevision | Return stage, target, returning and responsible users, mandatory feedback, corrective action, policy blocker/evidence, idempotency key and resubmission/version history. |

Existing TenderReports stores the immutable canonical PDF, SHA-256 hashes, verified source snapshot, template/signature-page metadata, supersession timestamp and rowversion. Foreign keys, unique signature section/user indexes, unique revision request IDs, tender transaction locks and rowversions protect concurrent transitions. Existing records were not reset, deleted or replaced. The deployment SQL is in `docs/report-lifecycle-migration.sql`; do not execute that non-idempotent script twice.

## Automated PDF reports

Report generation and authorized resubmission automatically create a structured Orange PDF from verified SQL Server records. It contains requirements/items, approved weight version/approval, supplier quotations and additional costs, technical and delivery terms, compliance/exclusions, supporting document references, every member's financial/technical scores and observations, exact weighted calculations, rankings/ties, recommendation, supplementary AI evidence and available approval/revision history.

PDFsharp/MigraDoc 6.2.4 controls section order, pagination, repeated table headers, long-cell continuation, fonts, Orange branding, page numbers and fixed signature positions. Users enter permitted justification/correction text, not layouts. The final signature page reserves four Evaluation Committee sections, a Manager section and four conditional Top Committee sections. Viewing copies identify non-required or policy-pending Top sections.

Canonical PDF bytes are stored in SQL Server, never wwwroot or unrestricted download storage. Source fingerprinting normalizes decimal representation without rounding, so SQL trailing zeros do not produce false tampering errors. Frozen snapshots preserve original display names while administrative name changes do not alter procurement input fingerprints.

## Signing and signature ordering

Users open the generated PDF, review it, explicitly confirm signing and submit the exact current document hash. The server binds each action to authenticated Identity user, actual permission role, assigned section, report/version, UTC time, decision, PDF hash and snapshot hash. Browser user/role fields cannot grant authority.

Four current-version committee signatures are required before finalization/Officer forwarding. Manager signing follows that gate. Assigned Top members sign their own decisions only after Manager approval, under the existing final-approval policy. Award checks the applicable current-version receipt set again. Duplicate identical signing actions are idempotent; conflicting actions cannot overwrite receipts. Ordinary EF changes/deletion of frozen report or signature payloads are rejected.

Default mode is explicitly **ElectronicAcknowledgment**: an authenticated database-backed approval, not a drawn image or certificate-backed digital signature.

Configured personal certificates use SHA-256 detached CMS signatures over an identity/version/time/document-hash manifest. The certificate must have an accessible private key, valid signing-time window and trusted system chain. Configured invalid credentials never silently downgrade to acknowledgment. One personal thumbprint cannot map to multiple users. Protected endpoints provide both the exact signed UTF-8 manifest and the CMS receipt.

This is **detached CMS over a document-hash manifest**, not an embedded PDF/PAdES signature. Receipt mathematical integrity is distinguished from current certificate trust/revocation status. Historical proof remains viewable; further approvals block when required current trust validation fails.

**Actual organizational trusted signing credentials were not supplied/configured. Certificate-backed production signing is not claimed complete or verified.** Qualified signatures, trusted external timestamps and PAdES/LTV archival validation are not implemented or claimed.

## Watermarking and protected access

Every report view/download validates the active account, current role and tender assignment/ownership, verifies canonical integrity and generates a personalized copy. Identifying name/login information, a stable user fingerprint and UTC access time occupy reserved side/footer margins, outside data and signature boxes. Full user ID, report/version, canonical hash, access-copy hash and access time are recorded in SQL audit logs.

Viewing copies project current receipts into the predefined locations without changing canonical signed bytes. Responses use private/no-store caching, nosniff and same-origin framing. Suppliers and unassigned weight approvers cannot access confidential committee PDFs or receipts. Cryptographic proof files remain unchanged for verification and are separately access-controlled.

## Return for Revision

Each formal request records current review stage, source session state, returning user, responsible owner, target, affected report/version, mandatory feedback, required corrective action, request UUID, UTC time and policy evidence/blocker. Supported review stages are Committee Review, Officer Handoff, Manager Review and Top Review.

The previously approved Manager-to-Officer narrative correction route remains available. Other routes require an explicit policy version, stage/target pair and authorized returning Identity IDs. Normal assignment/role checks still apply; Admin has no general override.

Undefined routes persist PolicyBlocked / RevisionBlocked. The original authorized returner may activate them only after an exact matching policy is configured. Return, activation and resubmission retries do not repeat the underlying transition.

Only the assigned responsible author/Officer may correct the permitted narrative. Resubmission recalculates from preserved original procurement inputs, creates a new PDF/report version and PreviousReportId link, preserves old decisions/feedback, marks old endorsements as superseded and requires four fresh signatures. No old signature is copied. Prior justification and correction history are preserved without recursively nesting whole JSON reports.

A permitted Top return loop also requires fresh Manager and Top decisions. Existing Top assignments use a new report-bound CommitteeRound, retaining old votes and preventing reuse toward award.

Changing approved weights, deadline-locked bids or finalized scores remains explicitly blocked pending a separately approved and implemented versioned procedure. Narrative route configuration cannot unlock those categories. No post-award reopening or Top return-voting majority was invented.

## Role-specific implementation

Controllers and review pages are inside the EvaluationCommittee, SourcingOfficer, SourcingManager and TopCommittee Areas. Only shared HTTP transport is in abstract ReportControllerBase. Layouts remain independent and unchanged by this extension.

Open `/<RoleArea>/Reports/Review/<reportId>` from the tender's report link. Committee members sign their own sections; the Officer forwards/corrects assigned reports; the Manager signs/decides separately from their evaluation seat; Top members sign only their assigned final decisions. Historical versions and feedback remain linked on the review page.

## Configuration

The migration is already applied locally. Other installations use the existing EF deployment process. Launch normally with:

```powershell
dotnet run --project Orange-Tendering-System --launch-profile https
```

Existing real accounts, role assignments, approved tender data, weight/final approval policies and Ollama configuration remain prerequisites. This extension does not provision unrelated accounts or procurement records.

`ReportPdf:FontDirectory` may remain null on Windows with Arial regular/bold installed. Other hosts should provide a trusted font directory containing Arial or DejaVu Sans regular/bold files. Production PDF generation is .NET; Python/Poppler are not production dependencies.

Signing defaults:

```json
"ReportSigning": {
  "RequireCertificateBacked": false,
  "StoreLocation": "CurrentUser",
  "UserCertificateThumbprints": {}
}
```

For actual trusted signing, provision approved personal credentials in the service identity's configured certificate store, map exact Identity IDs to unique thumbprints through protected deployment configuration, and set RequireCertificateBacked to true. Verify organizational identity enrollment, private-key access, trust/revocation connectivity and real signing/verification before declaring certificate-backed mode operational. Do not put private keys or passwords in source control or chat.

Example shape for a separately approved narrative return route (not a selected business policy):

```json
"ReportRevisionPolicy": {
  "Version": "<approved-policy-reference>",
  "Routes": [
    {
      "Source": "CommitteeReview",
      "Target": "ReportAuthor",
      "ReturningUserIds": ["<authorized-assigned-reviewer-ID>"]
    }
  ]
}
```

The route mechanism supports explicit delegated narrative-return authority. A group Top return-voting procedure would need its own approved rules and implementation. Legacy reports without generated PDFs/hashes fail closed rather than acquiring manufactured retrospective signatures; any such migration/reissue requires an approved procedure.

## Status and limits

Implemented: automated generation/layout, immutable SQL document storage, authenticated acknowledgments, optional CMS adapter, protected personalized copies, version-bound receipts, ordering/forwarding gates and controlled narrative revision loops.

Blocked/unverified: actual trusted signing credentials and production PKI, undefined return/amendment procedures, signed-in browser operation using real organizational accounts, and original V5 DOCX review because that source file was not supplied. Existing account/approval/Ollama setup blockers were not bypassed.

Before the user's instruction to stop testing, the last full run reported **83 passed, 0 failed, 0 skipped** (25 calculation/policy/metadata/cryptography checks and 58 real SQL Server integration checks). The retained log is `docs/REPORT_LIFECYCLE_TEST_RESULTS.txt`. A nine-page long-evidence fixture received layout review. A later read-only exact-manifest download addition compiled but was not re-run after the stop instruction. The latest completed build had zero errors/warnings. No further tests were run afterward. The test project was subsequently deleted in the workspace; it was not restored, and its stale Solution reference was removed.

The actual database received the additive migration only; synthetic integration fixtures were isolated. The generated sample under output/pdf contains test data, not a real tender. The system is not represented as production-ready while the credential, policy and operational prerequisites above remain unresolved.

## Main changed/created files for this extension

- `Models/Entities/ReportLifecycle.cs`, `TenderReport.cs`, `WeightedWorkflow.cs`.
- `Data/ApplicationDbContext.cs`, `Data/Migrations/20261113092128_AutomatedReportLifecycle.cs`, its Designer and model snapshot.
- `Services/TenderWorkflowService.cs`, `TenderWorkflowService.Reports.cs`.
- `Services/Reports/ReportContracts.cs`, `ReportCanonicalJson.cs`, `ReportCryptography.cs`, `ReportDocumentService.cs`, `OrangeReportRenderer.cs`.
- `Controllers/ReportControllerBase.cs`.
- Four Area `Controllers/ReportsController.cs` and `Views/Reports/Review.cshtml` files.
- EvaluationCommittee, SourcingManager and TopCommittee Workflow controller hash/consent parameters and Area Workflow report links.
- `ViewModels/ReportLifecycleModels.cs`, `WorkspaceNavigation.cs`, `Program.cs`, `appsettings.json`, the PDF package reference, Solution cleanup and generated-artifact ignores.
- This delivery record, retained historical result log and `docs/report-lifecycle-migration.sql`.
