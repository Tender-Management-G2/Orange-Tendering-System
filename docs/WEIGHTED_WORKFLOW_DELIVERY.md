> Historical pre-integration delivery record. For the current branch, configuration, verification and removal of the workflow test project, see [INTEGRATION_DELIVERY.md](INTEGRATION_DELIVERY.md). Statements about earlier application-database migrations describe that earlier work, not this integration.

> Current verified workflow and deployment status: [Final workflow delivery](FINAL_WORKFLOW_DELIVERY.md). Earlier status/configuration statements below are historical and superseded where that delivery describes a change.

# Weighted Tender Evaluation & Approval — delivery

Date: 13 November 2026. This records implementation and verification, not production certification.

## Scope

Only the requested weighted evaluation and approval extension, its tender/bid integration, role Areas, secure documents, AI assistance, notifications and tests are implemented here. The extra Purchase Request creation/review and automatic Identity provisioning work was reverted after the user clarified scope. No additional business modules are claimed complete.

## Database actually used

The application is connected to **SQL Server `localhost`, database `OrangeTenderingSystem`**, using its existing Windows-authenticated `Default Connection` setting. The separate `OrangeSourcingMvc10` LocalDB database was inspected during discovery and was not modified.

Migration **`20261113080931_WeightedTenderWorkflow` was applied successfully to OrangeTenderingSystem**. Its history now contains five migrations. The schema contains the original 30 tables plus **six** new tables. Existing business records were not deleted, reset, reseeded or replaced. The seven Identity role names were initialized during startup verification, but automatic provisioning code was removed when the user restricted scope. No accounts, passwords, role assignments or fictitious procurement records were created. Verification found zero application users and zero tenders in the actual database.

The readable deployment SQL is in `docs/weighted-workflow-migration.sql`. It starts after the previously applied `AddCommitteeResponsibilityAndOwnershipChecks` migration. Use EF migration history when updating another installation; do not run this non-idempotent SQL twice or run migration `Down` against live records.

## Six new tables and reused entities

| New table | Purpose and relationships |
|---|---|
| `WeightRound` | Immutable tender requirements snapshot/version, calculation, approver, policy snapshot and locked approved weights; belongs to existing `Tenders`. |
| `WeightProposal` | One authenticated member proposal per weight round; references `WeightRound`, `CommitteeMembers` and Identity. Unique by round/member and round/user. |
| `EvaluationSession` | One session per tendering round, approved weight version, original scoring round, optional revised report review round, AI report, UTC schedule, closure and frozen final-approval policy. |
| `EvaluationParticipation` | **Merged invitation and final-submission record** per session/member, with invitation timestamp and nullable final submission timestamp. Existing `EvaluationScores` hold the actual scores. |
| `SessionBid` | Relational snapshot of eligible offers for a session; unique session/bid, with foreign keys to existing bids. |
| `AiAnalysis` | Actual input evidence, model, run status, supplementary findings, elapsed time and human review metadata, linked to the tendering round. |

There is **no new AwardRecord table**. Existing `Tenders.AwardedBidId` and `AwardedAt` are reused, with `AwardedByUserId`, `AwardApprovalEvidenceJson` and `ClosedAt`. A tender primary key, serializable tender row lock, workflow-state checks and rowversion protect against repeated award.

The migration also adds tender estimated value, currency, critical classification and rowversion, and widens `EvaluationCriteria.Weight` and `CommitteeRounds` financial/technical weights from `decimal(9,4)` to `decimal(12,6)`. The `Up` migration has no table or data deletion. Foreign keys use restrictive deletion, unique indexes guard submissions, and SQL checks validate proposal totals, approved-weight metadata and session time windows.

Reused tables include PurchaseRequests, PurchaseRequestItems, PurchaseRequestReviews, Suppliers, TenderRounds, TenderItems, TenderInvitations, Bids, BidItems, Documents, CommitteeMembers, CommitteeRounds, EvaluationCriteria, EvaluationScores, CommitteeVotes, TenderReports, AuditLogs and Notifications. The repository has no separate V5 TechnicalEvaluations/FinancialEvaluations/TenderApprovals entities to reuse; scores and decisions use its actual existing equivalents.

## Implemented workflow

1. The originating Purchase Request must already be accepted and contain real line items. This feature validates and links that accepted request. Creating, revising and approving Purchase Requests remain the responsibility of the existing request module and are outside this implementation.
2. The officer creates tender requirements from an accepted request, copying real requested line items. Technical, commercial, delivery and mandatory compliance terms are required. Four distinct members are validated against live Identity roles, including a Sourcing Manager and a representative of the requesting department. Supporting requirement files can be uploaded before freezing the version.
3. Sending the weight request freezes requirements version 1 and notifies the four members. Each authenticated member submits exactly one final 0–100 financial/technical proposal totaling exactly 100. The browser provides complementary percentage assistance; C# performs authoritative validation.
4. Four proposals trigger exact arithmetic means and `Awaiting Weight Approval`. With no explicit approver policy the UI says `Awaiting Approval Policy`. Configured authority can approve once, freezing both weights, the requirements version, policy version, approver and UTC timestamp. No normal edit or overwrite operation exists.
5. Only locked approved weights allow bidding to open. Approved suppliers receive persisted invitations, accept/decline, submit item prices, technical details, delivery and commercial terms, and attach private documents before the server deadline. Submitted offers are immutable. Suppliers see only their own bids and attachments. The officer closes bidding after its deadline, records compliance and reasons, and resolves pending clarifications before evaluation.
6. Insufficient-offer rounds can be marked failed and a fresh round created without carrying invitations/bids forward. Normally three distinct submitted suppliers are required. The repository's approved exception permits two in a subsequent round after three prior failed rounds. Received non-compliant offers count for participation, but cannot be ranked.
7. The officer runs and reviews Ollama assistance, then schedules the evaluation with a supplied HTTPS Zoom link, UTC start/end, eligible offers, approved weights and four persisted member invitations. Before the start, documents are available and forms remain locked. Time checks on every submission enforce automatic opening and closing. A worker persists deadline closure every 15 seconds; authorization remains effective even if that worker is unavailable.
8. Each member atomically submits both scores and justification for every eligible bid exactly once. The officer can confirm manual closure with completion/missing counts and a reason. Incomplete sessions cannot produce a recommendation and do not use zero or a partial divisor.
9. Complete closed sessions produce exact weighted results and a versioned unified report: requirements, approved weights, original scores, per-member weighted results, ranking, compliance, supplementary AI evidence and justification. Exact top-score ties are explicit and block award. All four evaluation members endorse the same report version before officer forwarding.
10. The assigned Manager separately approves, rejects or returns the finalized report. Returns/rejections require reasons. A returned report can receive an **explicit textual correction version** linked to the original, preserving original scores and ranking and requiring four fresh endorsements. Changing scores is not part of this correction procedure.
11. The configured final policy determines whether Top Committee approval is required. When required, four distinct authorized members are assigned, separate from the Evaluation Committee, officer and manager. They vote on the report and management decision, never re-enter scores. Explicit quorum, approval threshold, tie outcome and rejection rules are enforced from a frozen policy snapshot.
12. The officer may record the official award and closure only after all applicable approvals, eligibility and exact ranking checks. Rejected/returned recommendations, missing approval, duplicate award and highest-score ties are blocked. Relevant users receive private in-app notifications.

Important states are persisted on existing Tender/TenderRound/CommitteeRound/TenderReport entities and the new WeightRound/EvaluationSession entities. `Awaiting Approval Policy` is derived from the pending record plus missing configuration; it cannot be bypassed by hiding or showing a button.

## Exact calculations and precision

- Validate every proposal: `Financial + Technical == 100m`, both in `[0m,100m]`.
- Financial average = sum of the four financial proposals / `4m`; technical average uses the same rule.
- Member score = `(FinancialScore * ApprovedFinancialWeight + TechnicalScore * ApprovedTechnicalWeight) / 100m`.
- Supplier final score = sum of **all four** member scores / `4m`.
- Rank by the exact decimal final score, descending. Equal scores share a rank; no arbitrary tie-breaker is used. Display formatting never determines rank.

Proposal and raw score inputs allow at most four decimal places, validated before persistence (`decimal(9,4)`). Dividing four such proposals by four can require six fractional places, so calculated/approved weights and copied criterion/committee weights use `decimal(12,6)`. Products/divisions and final averaging remain C# decimals and are not rounded between steps. Full results are preserved in report JSON; display uses consistent formatting. No floating-point official calculation is used.

Verified exact cases: 40/60, 30/70, 50/50, 40/60 => **40/60**; 90/80 at 40/60 => **84**; 84,88,83,92 => **86.75**. A fractional proposal case preserves `0.000025` exactly.

## Authorization and storage

All business controllers are inside their role Areas. The shared controller only dispatches navigation/notification links to the appropriate Area; its abstract base shares error handling and authorized downloads. Services contain calculations, authorization scope and transactions. Views use strongly typed models; writes bind input models rather than EF entities. Browser writes validate antiforgery tokens.

Live Identity account status and role checks combine with tender ownership, invitation ownership, committee membership, workflow state, deadline and prior-submission checks. Officer and Manager management access cannot alter another member's scores. Configurable weight-approval authority grants requirements/proposal review, not confidential bid/report access. Top voting excludes the officer and manager and requires explicit assignment.

Critical tender mutations acquire the same SQL Server `UPDLOCK,HOLDLOCK` tender row inside a serializable transaction. Final submission checks the deadline again after saving and before committing. Unique constraints and rowversions provide additional protection.

Files are stored under the application's private `App_Data/TenderDocuments`, outside `wwwroot`, with generated storage keys and authorized downloads. Path containment, file size/extension limits, attachment downloads and `nosniff` protect delivery. Uploaded files are ignored by Git. Requirements freeze after the first weight request; bid uploads stop at the bidding deadline. No financial business data is stored in browser storage.

## Role authorization summary

| Role / configured authority | Enforced scope |
|---|---|
| Sourcing Officer | Only their own tender: requirements, invitations, compliance, session setup/closure, report handoff and gated award. No editing committee scores. |
| Evaluation Committee | Only assigned tenders: one proposal and one complete final evaluation per authorized context; review and endorse reports. |
| Sourcing Manager | Their assigned evaluation seat is separate from the assigned manager's later approve/reject/return authority. No historical-score editing. |
| Supplier | Own company invitations, offer submission/status and own private documents only. |
| Top Committee | Four separate assigned members; only assigned members with the current role can vote. Officer and Manager cannot vote through management privileges. |
| Weight approver | Only identities/roles explicitly named by the configured policy; requires an active account. Approval policy does not confer confidential bid/AI/report access. |
| Admin / Requesting Department | No additional evaluation, weight approval or Top voting powers are inferred from these roles. Original business modules remain unchanged. |
## Ollama and meeting integration

`OllamaBidAnalysisService` calls the configured Ollama `/api/generate` endpoint. Neither address nor model is assumed. Timeout is configurable and bounded to 5–300 seconds. Actual bid values, financial totals, technical details, delivery and document references form the evidence. TXT/CSV and DOCX text extraction is bounded; XML DTDs are prohibited. Other formats, including PDF and images, are explicitly marked as not extracted and require manual review; their contents are never invented.

The model is instructed to return JSON observations with bid/document references, short supporting evidence and uncertainty. All findings remain unverified assistance until human review. AI cannot change weights, compliance, scores, decisions or awards. Failures are recorded on the analysis and do not modify bids. The test suite simulates a transport outage to verify that boundary; it does not claim successful live model inference.

Meeting setup saves a supplied Zoom URL and sends **in-app** invitations to the officer's four evaluators. It does not call Zoom or create an external meeting, and does not claim external email delivery.

## Verification and status

- **Implemented and tested:** decimal validation/formulas/ties; real SQL Server migrations, persistence, state transitions, account/role/ownership checks, proposal and evaluation uniqueness, concurrency, closure, original-score immutability, compliance gates, private document authorization, report versions, manager decisions, configurable Top policy and award/closure. Integration fixtures supply an accepted Purchase Request as the prerequisite; the tests then execute all eight requested tender stages through real SQL Server services.
- **Implemented but unverified with live external data:** successful Ollama inference and the quality/evidence accuracy of its output; actual human participation in the supplied Zoom meeting; authenticated browser layout/interaction review using real provisioned accounts. Razor compilation and public/anonymous HTTP protection were verified.
- **Blocked by missing configuration/policy:** actual weight approver identity/role, final approval scope/voting/tie/rejection rules, Ollama address/model and live account/supplier/department data. Missing-evaluation resolution and exact supplier-score tie resolution remain fail closed because no authorized procedure was provided.
- **Not implemented:** PDF/OCR/image extraction; exceptional amendments to already approved requirements/weights; renewed scoring after a final submission; supplier bid draft/revision editing. Original Purchase Request creation/review, Admin account provisioning, supplier registration/approval and department master-data pages remain outside this weighted-workflow implementation. No new code claims those unrelated scaffolded modules are complete.

`Sourcing.docx` was not present in the supplied attachment or repository. The implementation used the approved pasted extension and existing repository specification; the original brief still requires review when supplied. Older contradictory weight/Top-policy statements are marked superseded at the top of `TenderingOrangeSystem.md`.

The system is **not represented as production-ready** while these policy, external-service, provisioning and verification requirements remain open.

## Test commands and results

The tests are a dependency-free executable assertion runner, not an xUnit/VSTest project. Run the command below; `dotnet test` is not the command for this runner. The database suite uses the real SQL Server EF provider, real Identity stores and actual migrations, with no SQLite or mocked EF database.

```powershell
dotnet build --no-restore -m:1
$env:ORANGE_TEST_SQLSERVER = 'Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true'
dotnet run --project Orange-Tendering-System.Tests --no-build --no-restore
```

The SQL login must be allowed to create a dedicated test database. Every SQL run creates a random `OrangeWeightedTests_<guid>` database and removes only that generated test database afterward. It never puts fixtures into `OrangeTenderingSystem`. Unavailable SQL Server is explicitly reported as skipped rather than replaced by another provider.

Final scoped run: **61 passed, 0 failed, 0 skipped** (22 calculation/policy/controller checks and 39 SQL Server integration, authorization and concurrency checks). Individual test names are recorded in `docs/WEIGHTED_WORKFLOW_TEST_RESULTS.txt`. Build verification: zero errors and zero warnings. Migration model comparison reports no pending model changes.

A temporary real application startup against OrangeTenderingSystem succeeded. Public `/` returned 200; all seven checked protected workflow/notification URLs returned Identity login redirects for an anonymous client. During that verification the seven role names were initialized; the automatic seeder was subsequently removed to keep scope limited. The temporary verification server was stopped afterward.

## Running the workflow

1. From the repository root, restore packages and run `dotnet ef database update --project Orange-Tendering-System`. This migration is already applied to the selected local OrangeTenderingSystem database.
2. Provision real active, email-confirmed Identity accounts and role assignments through an authorized account-management process. Provide an active department, its requester, an officer, the four evaluation members, approved supplier companies and their linked accounts, plus four separate Top members if policy requires them. The actual database currently has no users; test users were never copied into it.
3. Configure `TenderApprovalPolicy` through deployment configuration/user secrets. Start from the null/empty fail-closed settings in appsettings. Set a policy version and the explicit weight approver user IDs or roles. This does not grant any ordinary officer/manager/admin automatic weight authority.
4. Set final approval scope to `All`, `None`, or `ThresholdOrCritical`. For threshold mode configure a positive threshold and its currency; currency mismatch blocks evaluation of that rule rather than inventing an exchange rate. When Top approval is required, explicitly set quorum 1–4, approvals required 1–quorum, rejection blocking behavior and Top tie outcome (`Block`, `Reject`, `Approve`). Four distinct Top members are still assigned. These are configuration capabilities, **not selected business policy defaults**. Supplier final-score ties always remain blocked independently.
5. Configure `Ollama:BaseUrl` and `Ollama:Model`, with optional `Ollama:TimeoutSeconds`. Keep connection credentials and internal service configuration in deployment secrets. A successful, reviewed AI analysis is required by the supplied session setup workflow; an outage preserves records and requires retry.
6. Run `dotnet run --project Orange-Tendering-System --launch-profile https`. Use the seven role Areas and their existing navigation. Start with an existing accepted Purchase Request, then use `/SourcingOfficer/Workflow/Create` to prepare its tender requirements. Weighted pages are at `/<RoleArea>/Workflow`; configured weight approvers use `/EvaluationCommittee/WeightApprovals`.
7. Follow the persisted stages above. Enter scheduling dates in local time; browser conversion posts explicit UTC offsets and server comparisons enforce the actual window. Reviewers can download authorized documents before the scoring window. Finalize all four committee endorsements, forward, record Manager and applicable Top decisions, then explicitly award/close.






## Actual file changes

### Modified existing files

- `.gitignore`
- `docs/TEAM_SETUP.md`
- `Orange-Tendering-System.slnx`
- `Orange-Tendering-System/appsettings.json`
- `Orange-Tendering-System/Areas/Admin/Views/_ViewStart.cshtml`
- `Orange-Tendering-System/Areas/Department/Views/_ViewStart.cshtml`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Controllers/CriteriaController.cs`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Controllers/EvaluationsController.cs`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Controllers/ReportsController.cs`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Views/_ViewStart.cshtml`
- `Orange-Tendering-System/Areas/SourcingManager/Controllers/ReportsController.cs`
- `Orange-Tendering-System/Areas/SourcingManager/Views/_ViewStart.cshtml`
- `Orange-Tendering-System/Areas/SourcingOfficer/Controllers/BidsController.cs`
- `Orange-Tendering-System/Areas/SourcingOfficer/Controllers/InvitationsController.cs`
- `Orange-Tendering-System/Areas/SourcingOfficer/Controllers/ReportsController.cs`
- `Orange-Tendering-System/Areas/SourcingOfficer/Controllers/TendersController.cs`
- `Orange-Tendering-System/Areas/SourcingOfficer/Views/_ViewStart.cshtml`
- `Orange-Tendering-System/Areas/Supplier/Controllers/InvitationsController.cs`
- `Orange-Tendering-System/Areas/Supplier/Controllers/OffersController.cs`
- `Orange-Tendering-System/Areas/TopCommittee/Controllers/DecisionsController.cs`
- `Orange-Tendering-System/Areas/TopCommittee/Views/_ViewStart.cshtml`
- `Orange-Tendering-System/Controllers/NotificationsController.cs`
- `Orange-Tendering-System/Data/ApplicationDbContext.cs`
- `Orange-Tendering-System/Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `Orange-Tendering-System/Models/Entities/CommitteeRound.cs`
- `Orange-Tendering-System/Models/Entities/CommitteeVote.cs`
- `Orange-Tendering-System/Models/Entities/EvaluationCriterion.cs`
- `Orange-Tendering-System/Models/Entities/Tender.cs`
- `Orange-Tendering-System/Program.cs`
- `Orange-Tendering-System/Services/README.md`
- `Orange-Tendering-System/ViewModels/WorkspaceNavigation.cs`
- `Orange-Tendering-System/Views/Notifications/Index.cshtml`
- `Orange-Tendering-System/Views/Shared/_Sidebar.cshtml`
- `README.md`
- `TenderingOrangeSystem.md`

### Created files

- `docs/WEIGHTED_WORKFLOW_DELIVERY.md`
- `docs/WEIGHTED_WORKFLOW_TEST_RESULTS.txt`
- `docs/weighted-workflow-migration.sql`
- `Orange-Tendering-System.Tests/Orange-Tendering-System.Tests.csproj`
- `Orange-Tendering-System.Tests/Program.cs`
- `Orange-Tendering-System.Tests/SqlWorkflowTests.cs`
- `Orange-Tendering-System/Areas/Admin/Views/Shared/_AdminLayout.cshtml`
- `Orange-Tendering-System/Areas/Department/Views/Shared/_DepartmentLayout.cshtml`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Controllers/WeightApprovalsController.cs`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Controllers/WorkflowController.cs`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Views/Shared/_EvaluationCommitteeLayout.cshtml`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Views/WeightApprovals/Details.cshtml`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Views/WeightApprovals/Index.cshtml`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Views/Workflow/Details.cshtml`
- `Orange-Tendering-System/Areas/EvaluationCommittee/Views/Workflow/Index.cshtml`
- `Orange-Tendering-System/Areas/SourcingManager/Controllers/WorkflowController.cs`
- `Orange-Tendering-System/Areas/SourcingManager/Views/Shared/_SourcingManagerLayout.cshtml`
- `Orange-Tendering-System/Areas/SourcingManager/Views/Workflow/Details.cshtml`
- `Orange-Tendering-System/Areas/SourcingManager/Views/Workflow/Index.cshtml`
- `Orange-Tendering-System/Areas/SourcingOfficer/Controllers/WorkflowController.cs`
- `Orange-Tendering-System/Areas/SourcingOfficer/Views/Shared/_SourcingOfficerLayout.cshtml`
- `Orange-Tendering-System/Areas/SourcingOfficer/Views/Workflow/Create.cshtml`
- `Orange-Tendering-System/Areas/SourcingOfficer/Views/Workflow/Details.cshtml`
- `Orange-Tendering-System/Areas/SourcingOfficer/Views/Workflow/Index.cshtml`
- `Orange-Tendering-System/Areas/Supplier/Controllers/WorkflowController.cs`
- `Orange-Tendering-System/Areas/Supplier/Views/Shared/_SupplierLayout.cshtml`
- `Orange-Tendering-System/Areas/Supplier/Views/Workflow/Details.cshtml`
- `Orange-Tendering-System/Areas/Supplier/Views/Workflow/Index.cshtml`
- `Orange-Tendering-System/Areas/TopCommittee/Controllers/WorkflowController.cs`
- `Orange-Tendering-System/Areas/TopCommittee/Views/Shared/_TopCommitteeLayout.cshtml`
- `Orange-Tendering-System/Areas/TopCommittee/Views/Workflow/Details.cshtml`
- `Orange-Tendering-System/Areas/TopCommittee/Views/Workflow/Index.cshtml`
- `Orange-Tendering-System/Controllers/WorkflowController.cs`
- `Orange-Tendering-System/Controllers/WorkflowControllerBase.cs`
- `Orange-Tendering-System/Data/Migrations/20261113080931_WeightedTenderWorkflow.cs`
- `Orange-Tendering-System/Data/Migrations/20261113080931_WeightedTenderWorkflow.Designer.cs`
- `Orange-Tendering-System/Models/Entities/WeightedWorkflow.cs`
- `Orange-Tendering-System/Services/EvaluationDeadlineWorker.cs`
- `Orange-Tendering-System/Services/OllamaBidAnalysisService.cs`
- `Orange-Tendering-System/Services/ProtectedDocumentStore.cs`
- `Orange-Tendering-System/Services/TenderWorkflowService.cs`
- `Orange-Tendering-System/Services/WeightedCalculation.cs`
- `Orange-Tendering-System/ViewModels/WeightedWorkflowModels.cs`
- `Orange-Tendering-System/Views/Shared/WorkflowError.cshtml`
