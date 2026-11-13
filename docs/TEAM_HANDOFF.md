# Team Handoff

Who builds what, where the files are, and what to watch out for. Paths are relative to the project folder
`Orange-Tendering-System/`. The full page list (with Trello links) is in [PAGE_OWNERSHIP.md](PAGE_OWNERSHIP.md);
every URL is in [ROUTE_MAP.md](ROUTE_MAP.md). **Business rules are in [`TenderingOrangeSystem.md`](../TenderingOrangeSystem.md)**
— the single product specification. The coverage and database review is in
[FINAL_SCAFFOLD_AND_DATABASE_AUDIT.md](FINAL_SCAFFOLD_AND_DATABASE_AUDIT.md). Local setup: [TEAM_SETUP.md](TEAM_SETUP.md).

## Before you start (everyone)

1. Pull `main`, build, and run. Public pages work immediately.
2. **Workspace pages need roles.** Every area controller has `[Authorize(Roles = ...)]` with the names in
   `Models/SystemRoles.cs`. The seven roles are seeded at startup on a migrated database; until a user is assigned a role, workspace pages
   correctly show *Access denied*. Do not remove `[Authorize]` to see your page — ask Admin for role assignment, then
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
| Department | `Areas/Department/Views/Dashboard/Index`, `PurchaseRequests/Index`, `Create`, `Details`, `Edit` (edit & resubmit a returned request — no separate card) |
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
Depends on: Officer full report (UNASSIGNED `SourcingOfficer/Reports/*`), Doulat (Top Committee voting).
Rules: the Officer's submitted report is **read-only** for the Manager — to correct it, return it with a comment and
the Officer submits a new version. The Manager edits only their **own draft executive summary** (Forward page, with
preview). Never edit committee scores or votes. Decisions are stored on the exact report version reviewed.

## Developer 3 — Ahmad (Authentication, Biometrics, Database, GitHub)

| Task | Where |
|---|---|
| User Registration | `Areas/Identity/Pages/Account/Register.cshtml(.cs)` — make it supplier registration: the disabled company section (name, registration no., tax no., email, phone, address, legal documents) must create a Pending `Supplier` + `Documents` + `Supplier` role |
| Login & Role Redirect | `Areas/Identity/Pages/Account/Login.cshtml(.cs)`, `Controllers/WorkspaceController` + `Views/Workspace/Index` |
| Forgot / Reset Password | `Areas/Identity/Pages/Account/ForgotPassword`, `ResetPassword` (SMTP `IEmailSender` implemented; runtime configuration required) |
| Fingerprint / Face | `Controllers/BiometricsController`, `Views/Biometrics/Fingerprint`, `Face`; disabled buttons on Login |
| Database Design & ERD | `Models/Entities/*`, `Data/ApplicationDbContext.cs` (an ERD diagram is still to be drawn) |
| Implementation & Seed Data | `Data/Migrations/*`; seven roles seeded by `Services/IdentityBootstrap.cs` (no demo users or passwords in Git) |
| GitHub & Team Workflow | repository, `.gitignore`, this document |

Already done in this scaffold: `Program.cs` now calls `.AddRoles<IdentityRole>()` — without it role checks never pass.
Rules: one authentication system (Identity); no fake biometric checks; no static photo as face verification.

### Ahmad's checklist — roles, initial Admin and accounts (blocks the whole team)

The Ahmad branch implements role seeding, supplier registration, OTP, email delivery, cookie gates, and first-login password state. Database and SMTP end-to-end verification remain environment dependent. Admin screens and supplier approval remain Rjoub's work.

1. **Seed the seven roles** idempotently at startup (or via an explicit command) from `Models/SystemRoles.cs`:
   `Admin`, `Requesting Department`, `Sourcing Officer`, `Supplier`, `Evaluation Committee`, `Sourcing Manager`,
   `Top Committee` — exact spelling, including spaces. Never create them by hand with different names.
2. **Initial Admin, securely:** create it only from values the developer supplies at run time (user secrets or
   environment variables), never from a password in Git, `appsettings*.json`, a migration or a seeder. Refuse to
   run if no password is supplied; do not silently promote an existing account.
3. **Admin creates internal employees** (`Areas/Admin/Users/Create`): `FullName`, email, `DepartmentId`,
   `IsActive`, temporary password handled securely; public registration must never create employees.
4. **Admin assigns and removes roles** (`Areas/Admin/Users/Edit`, `Roles/Index`) with `UserManager`; audit each
   change; keep at least one active Admin.
5. **Activate / deactivate** with `ApplicationUser.IsActive` (never delete users who have history); block sign-in
   for inactive users and refresh the security stamp so existing cookies stop working.
6. **Supplier registration → approval:** Register creates the account + a `Pending` `Supplier` + legal
   `Documents` + the `Supplier` role in one transaction; sign-in or participation is blocked until Admin sets the
   supplier to `Approved` (Rjoub builds the review page).
7. **Login redirect:** after sign-in, send each user to their workspace (`Views/Workspace/Index` lists them).
8. **Global role ≠ committee seat.** The `Evaluation Committee` and `Top Committee` roles only let a user open those
   workspaces. Scoring and voting on a tender additionally require a `CommitteeMember` row for **that tender**
   (`CommitteeType`, and for Evaluation the `Responsibility`: SourcingManager, DepartmentRepresentative,
   FinancialRepresentative, TechnicalRepresentative). Every vote/score check must use the assignment, not the role.
9. **The Sourcing Manager sits on the Evaluation Committee without losing the Sourcing Manager role.** Give that
   user both `Sourcing Manager` and `Evaluation Committee` roles (or let the Evaluation workspace accept a
   Sourcing Manager who has a `CommitteeMember` row with `Responsibility = SourcingManager`). The separate
   managerial review stage still uses the `Sourcing Manager` role. Decide one approach and document it.
10. **Demo accounts:** follow [DEMO_DATA_PLAN.md](DEMO_DATA_PLAN.md); passwords are set locally by each developer,
    never committed.

## Developer 4 — Amal (Tender Management)

Files: `Areas/SourcingOfficer/Views/Dashboard/Index`, `PurchaseRequests/Index`, `Tenders/Index`, `Details`, `Edit`
(both under "Manage Tenders"), `Create`, `SelectSuppliers`, `Timeline`, `Rounds` (no card — confirm), `Invitations/Index`, and
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
- **Officer Reports (7 pages)** — `Areas/SourcingOfficer/Views/Reports/Index`, `Comparison`, `Details`,
  `EvaluationResult`, `SubmitToManager`, `Returned`, `History`. Without them the report hand-offs between Officer,
  committee and Manager cannot be demonstrated.
- **Committee Assignment** — `Areas/SourcingOfficer/Views/Tenders/Committees`. The schema is ready
  (`CommitteeMember.Responsibility`, migration `AddCommitteeResponsibilityAndOwnershipChecks`). **Unresolved
  product-owner decision:** who may assign committee members — Sourcing Officer or Admin. The page sits in the
  Sourcing Officer area only until that is decided.
- **Assigned Evaluations** — `Areas/EvaluationCommittee/Views/Evaluations/Index` (committee entry point).
- **Reports, Analytics & Exports (6 pages)** — `Views/Analytics/*` (purchase requests, tenders, suppliers, bids &
  evaluation, governance; PDF/Excel entry points).
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
| Export bar | Disabled "Download PDF / Excel" buttons on report pages — see `Views/Analytics/*` |
| CSS `pg-*` classes | Panels, tables, forms, badges, notes — see `wwwroot/css/workspace-pages.css` |

## Remaining business implementation

Most domain workflows remain scaffolds, including tender rules, scoring, approvals, clarification messaging, analytics, document download authorization, Ollama, biometrics, and voice search. The Admin Users, Roles, and Supplier Review POST actions remain Rjoub's work.

### Authentication integration on Ahmad branch

- Apply `20261113091517_AddAuthenticationState` to each intended local database before running the app. It adds `MustChangePassword`, `EmailOtpSentAt`, and `EmailOtpFailedAttempts` to `AspNetUsers`; no tables are added (30 tables remain).
- Configure SMTP through User Secrets or environment variables (`Smtp:Host`, `Smtp:Port`, `Smtp:From`, optionally `Smtp:Username` and `Smtp:Password`). Registration rolls back if OTP delivery fails. Forgot Password reports an unavailable service if SMTP is missing.
- Optional first Admin bootstrap uses `BootstrapAdmin:Email`, `BootstrapAdmin:Password`, and optionally `BootstrapAdmin:FullName` from runtime configuration. It never promotes an existing non-Admin account and forces a password change on first sign-in.
- Rjoub owns Admin Users, Roles, and Supplier Review POST actions. For new employees, call `EmployeeProvisioning.CreateAsync(HttpContext, fullName, email, departmentId, roles)` from an Admin-only action; it emails a one-hour activation link and never sends a password. After changing roles, call `UserManager.UpdateSecurityStampAsync` so existing cookies refresh. Supplier approval sets `Supplier.Status = Approved` only after document review; pending and suspended suppliers are blocked on login and each authenticated request.
- Ahmad owns Identity pages, role seeding, SMTP integration, account-state cookie checks, and the provisioning contract. Fingerprint and Face ID remain deferred and disabled.
