# Team Handoff

Who builds what, where the files are, and what to watch out for. Paths are relative to the project folder
`Orange-Tendering-System/`. The full page list is in [PAGE_OWNERSHIP.md](PAGE_OWNERSHIP.md); every URL is in
[ROUTE_MAP.md](ROUTE_MAP.md).

## Before you start (everyone)

1. Pull `main`, build, and run. Public pages work immediately.
2. **Workspace pages need roles.** Every area controller has `[Authorize(Roles = ...)]` with the names in
   `Models/SystemRoles.cs`. The roles are not seeded yet (Ahmad's task), so until then every workspace page
   correctly shows *Access denied*. Do not remove `[Authorize]` to see your page — ask Ahmad for the seed, then
   give your local test account the role.
3. Open your page file and read its comment block: purpose, business role, future implementation list,
   dependencies. Remove `<partial name="_ScaffoldNotice" />` only when the page really works.
4. Keep the structure: **Controller → Service → `ApplicationDbContext`**. Put business rules in services
   (`Services/`), not in controllers or views. Use view models (`ViewModels/`) instead of passing entities.
5. Every action that changes data needs `[HttpPost, ValidateAntiForgeryToken]`, a server-side check of role
   **and** record ownership / committee assignment / workflow stage, and an `AuditLogs` entry.
6. Add a migration only when your feature needs a schema change, and tell the team in the PR.

## Developer 1 — Doulat (Department + Top Committee)

| Area | Files |
|---|---|
| Department | `Areas/Department/Views/Dashboard/Index`, `PurchaseRequests/Index`, `Create`, `Details` |
| Officer review step | `Areas/SourcingOfficer/Views/PurchaseRequests/Review` (business user: Sourcing Officer) |
| Top Committee | `Areas/TopCommittee/Views/Dashboard/Index`, `Decisions/ReviewEvaluation`, `Decision`, `History` |

Entities: `PurchaseRequest`, `PurchaseRequestItem`, `PurchaseRequestReview` (store `SubmittedSnapshotJson` on every
submission), `CommitteeRound`, `CommitteeMember`, `CommitteeVote`, `Tender` (award fields).
Depends on: Amal (tender creation uses accepted requests), Haya (executive summary → Top Committee round).
Rules: no department approval stage; Top Committee needs **4/4 Approve** of the same summary version; the award
(`Tender.AwardedBidId`) is separate from the recommendation.

## Developer 2 — Haya (Sourcing Manager)

Files: `Areas/SourcingManager/Views/Dashboard/Index`, `Reports/Index`, `Review`, `Approve`, `Reject`, `Return`,
`Forward`, `History`. The four decision pages share `Views/Shared/_ConfirmAction.cshtml`.
Entities: `TenderReport` (versions via `VersionNumber` + `PreviousReportId`; decision fields `ReviewedByUserId`,
`ReviewComment`, `ReviewedAt`), `CommitteeRound` (creating the Top round).
Depends on: Officer full report (UNASSIGNED `SourcingOfficer/Reports`), Doulat (Top Committee voting).
Rules: never edit committee scores or votes; a narrative correction is a **new** report version.

## Developer 3 — Ahmad (Authentication, Biometrics, Database, GitHub)

| Task | Where |
|---|---|
| User Registration | `Areas/Identity/Pages/Account/Register.cshtml(.cs)` — make it supplier registration (Pending `Supplier` + `Supplier` role) |
| Login & Role Redirect | `Areas/Identity/Pages/Account/Login.cshtml(.cs)`, `Controllers/WorkspaceController` + `Views/Workspace/Index` |
| Forgot / Reset Password | `Areas/Identity/Pages/Account/ForgotPassword`, `ResetPassword` (needs an `IEmailSender`) |
| Fingerprint / Face | `Controllers/BiometricsController`, `Views/Biometrics/Fingerprint`, `Face`; disabled buttons on Login |
| Database Design & ERD | `Models/Entities/*`, `Data/ApplicationDbContext.cs` (an ERD diagram is still to be drawn) |
| Implementation & Seed Data | `Data/Migrations/*`; **next step: seed the seven roles** from `Models/SystemRoles.cs` (no demo users or passwords in Git) |
| GitHub & Team Workflow | repository, `.gitignore`, this document |

Already done in this scaffold: `Program.cs` now calls `.AddRoles<IdentityRole>()` — without it role checks never pass.
Rules: one authentication system (Identity); no fake biometric checks; no static photo as face verification.

## Developer 4 — Amal (Tender Management)

Files: `Areas/SourcingOfficer/Views/Dashboard/Index`, `PurchaseRequests/Index`, `Tenders/Index`, `Create`,
`SelectSuppliers`, `Timeline`, `Rounds` (no card — confirm), `Invitations/Index`, and
`Areas/EvaluationCommittee/Views/Criteria/Index` (business user: Evaluation Committee).
Entities: `Tender`, `TenderRound`, `TenderItem`, `TenderInvitation`, `EvaluationCriterion`, `CommitteeRound`.
Depends on: Doulat (accepted requests), Saja (offers per round), Tariq (committee uses the criteria).
Rules: 3 submitted offers from 3 distinct suppliers per **round**; invitations do not count; old rounds stay; weights
are the committee's decision — never hardcode 60/40.

## Developer 5 — Saja (Bid Management)

Files: `Areas/SourcingOfficer/Views/Bids/Index`, `Details`, `Compliance`, `Compare`, `ForwardToCommittee`, `History`,
`Clarifications/Index`.
Entities: `Bid` (one row per revision; compliance columns incl. `MissingRequirements`), `BidItem`, `Document`,
`Clarification`, `ClarificationMessage`.
Depends on: Amal (rounds/invitations), Tariq (committee receives the round), Supplier portal (offers).
Rules: non-compliant offers count as received but cannot win; the minimum-offer check lives in a tested service;
suppliers never see each other's data.

## Developer 6 — Tariq (Public Site, Shared Layout, Evaluation Committee)

Public: `Views/Home/Index`, `About`, `HowItWorks`, `Team` (+ `wwwroot/data/team.json` — still 4 placeholders, replace
with the 7 real members), `Contact`.
Layout: `Views/Shared/_Layout`, `_WorkspaceLayout`, `_SupplierLayout`, `_Sidebar`, `_WorkspaceAccountMenu`,
`_PageHeader`, `ViewModels/WorkspaceNavigation.cs`, `wwwroot/css/workspace-pages.css`, `identity.css`.
Committee: `Areas/EvaluationCommittee/Views/Dashboard/Index`, `Evaluations/Technical`, `Financial`, `Scores`,
`AiAnalysis`, `History`, `Reports/Unified`.
Entities: `EvaluationScore`, `EvaluationCriterion`, `CommitteeVote`, `TenderReport` (`RecommendedBidId`,
`RankingSnapshotJson`).
Rules: scoring formula undecided — block finalization rather than guess; Ollama is advisory only.

## Developer 7 — Rjoub (Administration)

Files: `Areas/Admin/Views/Dashboard/Index`, `Users/Index`, `Create`, `Edit`, `Details`, `Roles/Index`,
`Departments/Index`, `Create`, `Edit`, `Suppliers/Index`, `Review`, `Details`, `Settings/Index`, `AuditLogs/Index`.
Entities: `ApplicationUser`, Identity roles, `Department`, `Supplier`, `Document` (verification), `AuditLog`.
Rules: internal accounts are created only here; deactivate instead of deleting; no `SystemSettings` table;
Admin has no committee vote.

## Unassigned — the team must decide an owner

- **Supplier portal (12 pages)** — `Areas/Supplier/Views/*`. Without it no supplier can submit an offer, so the
  workflow cannot be demonstrated. Highest priority to assign.
- **Officer Reports** — `Areas/SourcingOfficer/Views/Reports/Index` (comparison report and full report to the Manager).
- **Assigned Evaluations** — `Areas/EvaluationCommittee/Views/Evaluations/Index` (committee entry point).
- **Management Statistics** — `Areas/TopCommittee/Views/Statistics/Index`.
- **My Tasks + Voice Search** (mandatory feature) — `Views/Tasks/Index` and the disabled microphone in the topbar.
- **Privacy Policy** text — `Views/Home/Privacy`.

## ALL TEAM

- **Clarifications, Notifications & Audit Trail** — shared pages `Views/Clarifications/Index`, `Details`,
  `Views/Notifications/Index`. Agree on one `NotificationService` and one audit helper early; every module calls them.
- **End-to-End Workflow Testing** — follow the checklist at the end of [ROUTE_MAP.md](ROUTE_MAP.md). Each developer
  tests the happy path, a forbidden user, an invalid state transition and a duplicate submission/vote for their pages.
- **Final Bug Fixing & Demo Preparation** — keep [SCAFFOLD_STATUS.md](SCAFFOLD_STATUS.md) up to date as pages
  become real.

## Shared components you can reuse

| Component | Use |
|---|---|
| `_PageHeader` | Title + breadcrumb from `ViewData["Title"]`, `["WorkspaceSection"]`, `["Description"]` |
| `_ScaffoldNotice` | The "UI scaffold" banner — remove when the page works |
| `_EmptyState` | `<partial name="_EmptyState" model='"No bids yet."' />` |
| `_ConfirmAction` | Decision panel (`ConfirmActionModel`) for approve / reject / return / forward / vote |
| `_ApprovalTracker` | Four-seat unanimous approval status (`"Evaluation Committee"` or `"Top Committee"`) |
| CSS `pg-*` classes | Panels, tables, forms, badges, notes — see `wwwroot/css/workspace-pages.css` |

## Not implemented anywhere yet

All database reads and writes, every POST action, the workflow rules (3-offer gate, retendering, 4/4 votes,
report versioning), scoring, notifications, clarification messaging, audit writing, file upload/download,
Ollama, biometrics, voice search, email delivery and role seeding.
