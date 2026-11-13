# Final Scaffold and Database Audit

**Date:** 13 November 2026 · **Baseline:** `main` at `78dd700` plus the uncommitted specification v2.0
**Authoritative source:** [`TenderingOrangeSystem.md`](../TenderingOrangeSystem.md) v2.0 (read in full).
Paths below are relative to the project folder `Orange-Tendering-System/` unless they start with `docs/`.

## 1. Sources and limitations

| Source | Status |
|---|---|
| `TenderingOrangeSystem.md` v2.0 | Read completely. Used as the single source of truth. |
| Original brief `Sourcing_Tender_Management_System_English_Brief_V5 (1).docx` | **Not available** in the repository or under the project folder. Coverage of the original brief was checked only through the spec's own traceability (§23), not against the DOCX itself. |
| Team README `Tender_Management_Team_README (1).md` | **Not available as a file.** Its 60 cards and Trello URLs are reproduced in spec §18 and were used from there. |
| `docs/WORKFLOW.md` and the other old `docs/*` specification files | Not present, **not used, not recreated**. The only stale references were in the local, git-ignored `CLAUDE.md`; they now point to the spec. |
| Code, migrations, live SQL Server `OrangeTenderingSystem` | Inspected directly (read-only). |

## 2. Executive assessment

- **Architecture:** sound and appropriately simple — one MVC project, 7 role Areas, thin GET-only controllers,
  three layouts, shared partials, Identity unchanged, Controller → (future) Service → `ApplicationDbContext`.
- **Seven-developer readiness:** every one of the 60 Trello cards maps to concrete files with the real Trello URL
  in the file comment. Areas and controllers rarely overlap, so parallel work is practical.
- **Scaffold completeness before this audit:** *not* complete. Missing were the Department edit/resubmit page,
  tender details/edit, committee assignment, the Officer report lifecycle, report/export pages, supplier
  registration fields and several technical/financial evaluation fields. One page contradicted the spec (the
  Manager could edit the Officer's submitted report). All are fixed now (§4).
- **Database:** adequate for the whole workflow. The one small schema gap (committee member responsibility) and the
  two recommended CHECK constraints were **applied on 13 Nov 2026** (§11); everything else is application logic.
  **No redesign is needed.**
- **Must happen before feature work:** seed the seven roles (Ahmad); assign owners to the Supplier portal, Officer
  reports, committee assignment, analytics, My Tasks/voice search; decide who assigns committees.

## 3. Workflow coverage (spec §4)

"Before" = state at `78dd700`. Every listed view exists and renders; none contains business logic.

| # | Step | Controller / action | View(s) | Developer | Before | After |
|---|---|---|---|---|---|---|
| 1–3 | Department drafts request with items, justification, priority, date, attachments; submits to Officer | `Department/PurchaseRequests.Create` | `Areas/Department/Views/PurchaseRequests/Create` | Doulat | Existed | — |
| 4–5 | Officer reviews: accept / return / reject / ask clarification | `SourcingOfficer/PurchaseRequests.Review` | `…/PurchaseRequests/Review` | Doulat | Existed | Clarification link added |
| 6 | Department edits and resubmits a returned request; history kept | `Department/PurchaseRequests.Edit`, `.Details` | `…/PurchaseRequests/Edit`, `Details` | Doulat | **Missing** | **Added** |
| 7–8 | Officer creates and configures the tender | `SourcingOfficer/Tenders.Create`, `.Details`, `.Edit` | `…/Tenders/Create`, `Details`, `Edit` | Amal | Create only | **Details, Edit added** |
| — | Assign 4 Evaluation + 4 separate Top Committee members | `SourcingOfficer/Tenders.Committees` | `…/Tenders/Committees` | UNASSIGNED | **Missing** | **Added** |
| 9–10 | Select approved suppliers; invite per round | `SourcingOfficer/Tenders.SelectSuppliers`, `Invitations.Index` | `…/Tenders/SelectSuppliers`, `…/Invitations/Index` | Amal | Existed | — |
| 11 | Supplier sees own invitations; accept/decline/ignore | `Supplier/Invitations.Index`, `.Details`, `.Respond` | `Areas/Supplier/Views/Invitations/*` | UNASSIGNED | Existed | — |
| 12 | Supplier drafts/submits offers (prices, specs, delivery, costs, documents) | `Supplier/Offers.Create`, `.Edit`, `.Index`, `.Details` | `Areas/Supplier/Views/Offers/*` | UNASSIGNED | Existed | — |
| 13 | Officer tracks round and offers | `SourcingOfficer/Tenders.Timeline`, `Bids.Index` | `…/Tenders/Timeline`, `…/Bids/Index` | Amal, Saja | Existed | — |
| 14–16 | <3 distinct offers → NEW round; history kept; 2-offer exception | `SourcingOfficer/Tenders.Rounds` | `…/Tenders/Rounds` | Amal (no card) | Existed | Note aligned with spec §4.3 |
| 17 | Compliance review with reasons, missing requirements, reviewer, date | `SourcingOfficer/Bids.Compliance` | `…/Bids/Compliance` | Saja | Existed | — |
| 18 | Compare received / eligible / excluded offers | `SourcingOfficer/Bids.Compare` | `…/Bids/Compare` | Saja | Existed | — |
| 19 | Officer writes the initial comparison report | `SourcingOfficer/Reports.Comparison` | `…/Reports/Comparison` | UNASSIGNED | **Missing** | **Added** |
| 20 | Forward round and report to the 4-member committee | `SourcingOfficer/Bids.ForwardToCommittee` | `…/Bids/ForwardToCommittee` | Saja | Existed | Card title corrected |
| 21 | Committee reviews technical documents, financials, Officer comparison | `EvaluationCommittee/Evaluations.Index`, `.Technical`, `.Financial` | `Areas/EvaluationCommittee/Views/Evaluations/*` | UNASSIGNED / Tariq | Existed | Members, evidence, itemized costs added |
| 22 | Committee sets/confirms criteria and weights | `EvaluationCommittee/Criteria.Index` | `…/Criteria/Index` | Amal | Existed | — |
| 23 | Members score and record observations | `Evaluations.Technical`, `.Financial` | same | Tariq | Existed | Improved (§10 fields) |
| 24 | Transparent ranking (formula not approved) | `Evaluations.Scores` | `…/Evaluations/Scores` | Tariq | Existed | — |
| 25 | 4/4 approval of the same evaluation round | `EvaluationCommittee/Reports.Unified` + `_ApprovalTracker` | `…/Reports/Unified` | Tariq | Existed | — |
| 26 | Result returns to the Officer | `SourcingOfficer/Reports.EvaluationResult` | `…/Reports/EvaluationResult` | UNASSIGNED | **Missing** | **Added** |
| 27 | Officer submits the full report to the Manager | `SourcingOfficer/Reports.SubmitToManager` | `…/Reports/SubmitToManager` | UNASSIGNED | **Missing** | **Added** |
| 28 | Manager reviews; approves / returns / rejects | `SourcingManager/Reports.Review`, `.Approve`, `.Return`, `.Reject` | `Areas/SourcingManager/Views/Reports/*` | Haya | Existed | **Review corrected** (report read-only) |
| 29 | Returned reports corrected as new immutable versions | `SourcingOfficer/Reports.Returned`, `.History`, `.Details` | `…/Reports/Returned`, `History`, `Details` | UNASSIGNED | **Missing** | **Added** |
| 30 | Manager writes/forwards the executive summary | `SourcingManager/Reports.Forward` | `…/Reports/Forward` | Haya | Existed | Preview added |
| 31 | Top Committee reviews the summary | `TopCommittee/Decisions.ReviewEvaluation` | `Areas/TopCommittee/Views/Decisions/ReviewEvaluation` | Doulat | Existed | — |
| 32–33 | Four distinct members vote; 4/4 required | `TopCommittee/Decisions.Decision` | `…/Decisions/Decision` | Doulat | Existed | — |
| 34 | Award recorded, distinct from recommendation | `Decisions.Decision` (note), `SourcingOfficer/Tenders.Details` (outcome panel) | as listed | Doulat, Amal | Partial | Outcome panel added |
| 35 | Tender closed; participants notified | `Notifications.Index` (shared), `Tenders.Details` | `Views/Notifications/Index` | ALL TEAM | Existed | — |
| 36 | All versions, votes, decisions, clarifications, audit kept | Department `Details`, Officer `Reports.History`, EC `Evaluations.History`, SM `Reports.History`, TC `Decisions.History`, `Admin/AuditLogs`, `Analytics.Governance` | as listed | various | Partial | Report history + governance added |

### Mandatory features and other required areas

| Requirement | Location | Developer | Before | After |
|---|---|---|---|---|
| Supplier registration (company, reg. no., tax no., email, phone, address, legal documents, pending status) | `Areas/Identity/Pages/Account/Register` | Ahmad | Name, reg. no., phone only | **All fields + status explanation** (disabled; basic Identity account still works) |
| Face + fingerprint sign-in | `Login` (disabled buttons), `Views/Biometrics/Fingerprint`, `Face` | Ahmad | Existed | — |
| Ollama advisory analysis | `EvaluationCommittee/Evaluations.AiAnalysis` | Tariq | Existed | — |
| Voice search in My Tasks | `Views/Tasks/Index` + topbar mic | UNASSIGNED | Existed | — |
| Clarifications (5 role pairs, threads, replies, attachments) | `Views/Clarifications/Index`, `Details`; Officer `Clarifications.Index`; Supplier `Clarifications.Index` | ALL TEAM / Saja / UNASSIGNED | Existed | Links added from review/evaluation pages |
| Notifications, audit visibility | `Views/Notifications/Index`, `Admin/AuditLogs.Index` | ALL TEAM, Rjoub | Existed | — |
| Reports & exports (spec §16 categories) | `Views/Analytics/Index`, `PurchaseRequests`, `Tenders`, `Suppliers`, `Evaluations`, `Governance`; TC `Statistics.Index` | UNASSIGNED | Only TC statistics | **6 pages added** (disabled PDF/Excel) |
| Supplier downloadable personal history | `Supplier/Offers.History` | UNASSIGNED | No export | **Export entry point added** |
| Public site: logo, nav, login, registration, hero message, six steps, statistics placeholders | `Views/Home/*`, `_Header` | Tariq | Hero message missing | **Hero message added**, stats caption and step 2 wording fixed |
| Optional QR / signature / watermark | — | — | Deferred | Deferred (spec §22; at least two may still be required before final grading) |

Top Committee "assigned final decisions" are listed on its Dashboard (Doulat's card); no extra list page was added.

## 4. Corrections made in this audit

- **New GET actions and views (16):** `Department/PurchaseRequests/Edit`; `SourcingOfficer/Tenders/Details`,
  `Edit`, `Committees`; `SourcingOfficer/Reports/Comparison`, `Details`, `EvaluationResult`, `SubmitToManager`,
  `Returned`, `History`; `Analytics/Index`, `PurchaseRequests`, `Tenders`, `Suppliers`, `Evaluations`, `Governance`.
- **New controller (1):** `Controllers/AnalyticsController.cs` (shared, roles Admin / Sourcing Officer / Sourcing
  Manager / Top Committee). All other new actions reuse existing controllers.
- **Improved views:** Officer request review (clarification link), Tender rounds and Forward-to-committee notes
  (spec §4.3 wording), EC Assigned Evaluations (members + required composition), Technical evaluation (proposal,
  spec deviations, documents/images/videos/sample records, non-compliance), Financial evaluation (itemized
  quotation, taxes, transport, installation, maintenance, other, currency, terms, validity, delivery, comparison),
  **Manager review now read-only** (spec §4.6.4), Manager forward (summary preview), Supplier offer history
  (export), Identity Register (full supplier fields), public hero/stats/How-It-Works wording.
- **Navigation:** "Analytics & Exports" added to the Admin, Sourcing Officer, Sourcing Manager and Top Committee menus.
- **Ownership:** all 60 Trello URLs filled in every comment and in `docs/PAGE_OWNERSHIP.md`; Saja's card renamed to
  its real title "Forward Bids to Technical & Financial Committee".
- **Docs:** `PAGE_OWNERSHIP.md` and `ROUTE_MAP.md` regenerated; `TEAM_HANDOFF.md` (Manager rule corrected),
  `SCAFFOLD_STATUS.md`, `LAYOUT_INTEGRATION.md` updated; stale spec references in the local `CLAUDE.md` fixed.
- **Not changed:** entities, `ApplicationDbContext`, migrations, database, Identity behavior, layout design.

## 5. Ownership still to decide (no developer assigned)

| Work | Files | Why it matters |
|---|---|---|
| Supplier portal | `Areas/Supplier/**` (12 views) | No offers → no demonstrable workflow. **Highest priority.** |
| Officer reports | `Areas/SourcingOfficer/Views/Reports/*` (7 views) | Report hand-offs Officer ↔ committee ↔ Manager |
| Committee assignment | `Areas/SourcingOfficer/Views/Tenders/Committees` | Votes and scores need assigned members; business role (Officer or Admin) also undecided |
| Assigned evaluations | `Areas/EvaluationCommittee/Views/Evaluations/Index` | Committee entry point |
| Reports & exports | `Views/Analytics/*` (6 views) | Required report categories, PDF/Excel |
| Management statistics | `Areas/TopCommittee/Views/Statistics/Index` | Required Top Committee report |
| My Tasks + voice search | `Views/Tasks/Index` | Mandatory feature |
| Privacy policy text | `Views/Home/Privacy` | Content |

## 6. Database inventory (verified on live SQL Server, read-only)

*State at the audit (before §11). The current state is in §11.*

- **30 tables** = **22 domain** + **7 Identity** + **1** `__EFMigrationsHistory`.
- Domain: `AuditLogs`, `BidItems`, `Bids`, `ClarificationMessages`, `Clarifications`, `CommitteeMembers`,
  `CommitteeRounds`, `CommitteeVotes`, `Departments`, `Documents`, `EvaluationCriteria`, `EvaluationScores`,
  `Notifications`, `PurchaseRequestItems`, `PurchaseRequestReviews`, `PurchaseRequests`, `Suppliers`,
  `TenderInvitations`, `TenderItems`, `TenderReports`, `TenderRounds`, `Tenders`.
- Identity: `AspNetRoleClaims`, `AspNetRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserRoles`,
  `AspNetUsers` (+ `FullName`, `DepartmentId`, `SupplierId`, `IsActive`), `AspNetUserTokens`.
- Migrations applied: `00000000000000_CreateIdentitySchema`, `20261112220609_InitialOrangeTenderingSchema`,
  `20261112221808_AddBidComplianceAndRequestSnapshot`. `dotnet ef migrations has-pending-model-changes`: **none**.
- **59 foreign keys:** 6 Identity cascades; every domain FK is `Restrict` (`ApplicationDbContext.OnModelCreating`).
- **12 unique indexes:** `TenderRounds(TenderId, RoundNumber)`, `TenderInvitations(TenderRoundId, SupplierId)`,
  `Bids(TenderInvitationId, RevisionNumber)`, `BidItems(BidId, TenderItemId)`, `CommitteeMembers(TenderId, UserId)`,
  `CommitteeRounds(TenderRoundId, CommitteeType, RoundNumber)`, `CommitteeVotes(CommitteeRoundId, CommitteeMemberId)`,
  `EvaluationScores(BidId, EvaluationCriterionId, CommitteeMemberId)`, `TenderReports(TenderRoundId, ReportType,
  VersionNumber)`, `PurchaseRequestReviews(PurchaseRequestId, SubmissionNumber)`, `Suppliers(RegistrationNumber)`,
  `Departments(Name)`.
- **0 CHECK constraints.** Decimals: money `(18,2)`, quantities `(18,3)`, weights/scores `(9,4)`. 0 data rows.

## 7. Database findings

Severity: **A** critical schema gap · **B** recommended constraint/index · **C** application-logic rule ·
**D** deferred until a design decision · **E** no issue. The finding A and the two `Document` / `Clarification`
B findings were approved and **applied** (§11); everything else below is unchanged and remains a recommendation.

| Sev. | Entity / relationship | Problem | Impact | Smallest recommendation | Schema change? |
|---|---|---|---|---|---|
| **A** (small) — ✅ applied | `CommitteeMember` (`TenderId`, `CommitteeType`, `UserId`, `AssignedAt`) | The confirmed Evaluation composition — Sourcing Manager, Department, **Financial** and **Technical** representatives (spec §4.5) — cannot be recorded. Financial and Technical members share the "Evaluation Committee" role and the responsibility is per tender, so roles or claims cannot express it. | Cannot validate or display the required composition; cannot later give members different criteria subsets (open decision). | Add a nullable enum `CommitteeMember.Responsibility` (`SourcingManager`, `DepartmentRepresentative`, `FinancialRepresentative`, `TechnicalRepresentative`) + filtered unique index `(TenderId, CommitteeType, Responsibility)`. One small migration, before committee assignment is built. | Yes — 1 nullable column + 1 index |
| B — ✅ applied | `Document` (6 nullable owner FKs) | Exactly one owner is not enforced (spec §7.2 already flags it). | A file could belong to two contexts → wrong access check, possible cross-supplier exposure. | CHECK: number of non-null `SupplierId, PurchaseRequestId, TenderId, BidId, ClarificationMessageId, TenderReportId` = 1. Add before uploads are implemented. | Yes — CHECK only |
| B — ✅ applied | `Clarification` (`PurchaseRequestId?`, `TenderId?`, `BidId?`) | A thread can have no context. | Unscoped threads break role/stage authorization. | CHECK: at least one context column is not null. | Yes — CHECK only |
| B | Quantities, prices, costs, scores, numbers | No positive/range checks (`BidItems.UnitPrice`, `Bids.*Cost`, `*.Quantity`, `EvaluationScores.Score`, `RoundNumber`, `VersionNumber`, …). | Bad data possible if a service misses validation. | Optional CHECKs (`Quantity > 0`, prices/costs ≥ 0, numbers ≥ 1). Services must validate anyway. | Optional |
| C | Minimum-offer gate | 3 distinct submitted suppliers per round (or 2 after the allowed failures) is not a constraint. | — | Service: count distinct `TenderInvitation.SupplierId` with a Submitted `Bid` in the round; unique `(TenderRoundId, SupplierId)` already makes "distinct" reliable. | No |
| C | `CommitteeVote` / `EvaluationScore` context | FKs do not prove that member, round, criterion and bid belong to the same tender and committee type, or that the bid is compliant. | — | Service checks before every vote/score. | No |
| C | `BidItem.TenderItemId`, `TenderReport.RecommendedBidId`, `Tender.AwardedBidId` | Not guaranteed to belong to the same tender (path Bid → Invitation → Round → Tender is too long for an FK). | — | Service checks; award only after 4/4 Top approval of the same summary. | No |
| C | Committee size | "Exactly 4" per committee is not a constraint. | — | Service check when assigning and before opening a committee round. | No |
| C | Report/submission immutability | Rows can technically be updated. | — | Services never update a Submitted `TenderReport`, `PurchaseRequestReview` snapshot, submitted `Bid` or `CommitteeVote`; corrections create new rows. | No |
| C | Supplier isolation | — | — | Every supplier query filters by `ApplicationUser.SupplierId` through `TenderInvitation.SupplierId`; documents checked on every download. | No |
| D | Biometrics | No storage yet (by design). | — | Add credential metadata only after the provider/modality decision (spec §14.1). | Later |
| D | Ollama | No `AiAnalysisRuns` (by design). | — | Add only if retention becomes a requirement; human-included text goes into report content. | Later |
| D | `TenderItems` per tender, not per round | Items cannot differ between rounds. | Only if items may change on retendering (open, spec §21). | If yes: add `TenderRoundId` to items or snapshot items per round. | Later |
| D | Committee membership changes | Tender-level assignment; no history if a member is replaced mid-process. | Old votes keep their member rows (Restrict), so history is safe. | Add `RemovedAt`/`IsActive` only if replacement becomes a requirement. | Later |
| E | Retendering | — | — | `TenderRound` (`RoundNumber`, `Status` incl. `Failed`, dates) + per-round `TenderInvitations` + unique `(TenderId, RoundNumber)` fully support new-round retendering and either counting convention. | No |
| E | Bid revisions + compliance | — | — | `Bid` row per revision (unique `(TenderInvitationId, RevisionNumber)`); compliance fields `ComplianceStatus`, `ComplianceNotes`, `MissingRequirements`, `ComplianceCheckedByUserId`, `ComplianceCheckedAt` per revision. | No |
| E | Committee separation | — | — | Unique `CommitteeMembers(TenderId, UserId)` **already enforces in SQL** that one person cannot sit on both committees of the same tender. | No |

## 8. Answers to the ten database questions

1. **Documents — exactly one owner?** Yes. Six nullable FKs exist (`Document.SupplierId … TenderReportId`);
   **now enforced** by `CK_Documents_ExactlyOneOwner` (§11). Download authorization remains application logic.
2. **Request snapshots sufficient?** Yes. `PurchaseRequestReview.SubmittedSnapshotJson` (required) per
   `SubmissionNumber` preserves title, justification, priority, date and all items. Attachments are linked to the
   request (`Document.PurchaseRequestId`), not to a submission — the snapshot should list document ids/names and
   documents must never be deleted (C).
3. **Bid revisions and compliance history?** Revisions: yes (`Bid.RevisionNumber`, unique per invitation).
   Compliance is stored per revision; re-checking the *same* revision overwrites its compliance fields, so earlier
   values of that revision survive only in `AuditLogs` old/new values. Adequate (C); a separate table only if a
   formal re-check history is required (D).
4. **4/4 decision history?** Yes. `CommitteeVote` (unique per `CommitteeRoundId` + `CommitteeMemberId`) with
   restricted deletes; a rejection leads to a new `CommitteeRound` (`RoundNumber`), so old votes stay. The 4/4
   rule itself is C.
5. **Separate committee membership?** Separation: yes, enforced by the unique `(TenderId, UserId)` index. Exactly
   four: C. Required responsibilities (Manager / Department / Financial / Technical): **now stored** in
   `CommitteeMember.Responsibility`, one person per responsibility per tender (filtered unique index, §11).
6. **Manager decision on the exact version?** Yes. `TenderReport.ReviewedByUserId`, `ReviewComment`, `ReviewedAt`
   and `Status` are on each version row; corrections are new rows linked by `PreviousReportId`.
7. **Award history?** Derivable: `Tender.AwardedBidId` + `AwardedAt`, the Approved Top `CommitteeRound` (its
   `TenderReportId` = the executive summary) and its four `CommitteeVotes`. Recommendation stays separate in
   `TenderReport.RecommendedBidId`. A reversal would only be visible in `AuditLogs` (not a current requirement).
8. **Retendering — more tables?** No. `TenderRounds` is sufficient (finding E).
9. **Biometrics / Ollama fields now?** No — later, after the provider/retention decisions (D).
10. **Original reports supported by existing data?** Yes: counts and review times (`PurchaseRequests`,
    `PurchaseRequestReviews.SubmittedAt/ReviewedAt`), participation and zero-bid rounds (`TenderInvitations`,
    `Bids`), evaluation delays (`CommitteeRounds.StartedAt/CompletedAt`), document expiry (`Documents.ExpiresAt`,
    `VerificationStatus`), received vs eligible vs excluded (`Bid.ComplianceStatus`), votes and decisions, awards and
    award frequency per supplier, values from `BidItems` + bid costs. Contracts only where stored as `Documents`.

## 9. Requirement preservation

The spec keeps all functional requirements of the original brief (spec §23) with these **owner-approved
deviations**: new-round retendering instead of extending the same round; Evaluation Committee sets weights during
evaluation; Top Committee approval mandatory for every award (no value threshold); Sourcing Manager is one of the
four evaluators but reviews separately; submitted reports immutable; optional features deferred; code enums differ
from the brief's illustrative statuses (spec §6).

Genuine gaps or open conflicts found:
1. ~~Committee member responsibility cannot be stored (finding A).~~ Resolved in §11.
2. Who assigns committees (Sourcing Officer or Admin) is not defined anywhere.
3. Coverage against the original DOCX could not be verified directly (file unavailable).
4. Contact form: still a 501 placeholder; storage/delivery decision open (spec §21).
5. Invitation `Expired` status from the brief is derived from deadlines, not stored (documented in spec §6).

## 10. Decisions needed before implementation

1. ~~Approve the `CommitteeMember.Responsibility` column~~ — approved and applied (§11).
2. Who assigns committees — Sourcing Officer or Admin (**still open**).
3. Whether the initial tender round counts among the "three unsuccessful attempts".
4. Scoring method, scale, per-member aggregation, price normalization, ties (spec §21).
5. Owners for the unassigned work in §5.
6. ~~Document / Clarification CHECK constraints~~ — applied (§11). Optional positive-value CHECKs remain a
   recommendation only.
7. Biometric approach; Ollama retention; item changes between rounds; multi-currency policy.
8. How a Sourcing Manager enters the Evaluation workspace: hold both `Sourcing Manager` and `Evaluation Committee`
   roles, or let the evaluation pages accept a Sourcing Manager with an assigned `Responsibility = SourcingManager`
   seat (see Ahmad's checklist in `docs/TEAM_HANDOFF.md`).
9. **`CLAUDE.md`:** it contains only safe, team-wide engineering instructions (no secrets or local paths), so
   tracking it in Git would help every developer. It is ignored today at the project owner's explicit request;
   that rule was kept. Re-enable tracking only if the team agrees.

## 11. Database preparation applied (13 Nov 2026)

Migration **`20261113000510_AddCommitteeResponsibilityAndOwnershipChecks`** — additive only (no column dropped or
altered, no data touched). Applied to the local development database `OrangeTenderingSystem` on `localhost` after
confirming the target, the existing migration history and that the affected tables were empty.

| Change | Code | Database object |
|---|---|---|
| Evaluation responsibility | `Models/Entities/Enums.cs`: `CommitteeResponsibility { SourcingManager, DepartmentRepresentative, FinancialRepresentative, TechnicalRepresentative }`; `CommitteeMember.Responsibility` (`CommitteeResponsibility?`) | `CommitteeMembers.Responsibility nvarchar(30) NULL` (string-backed like every enum) |
| One person per responsibility | `ApplicationDbContext`: `HasIndex(TenderId, CommitteeType, Responsibility).IsUnique().HasFilter("[Responsibility] IS NOT NULL")` | `IX_CommitteeMembers_TenderId_CommitteeType_Responsibility` (unique, filtered). Top Committee rows keep `NULL` and are ignored. The existing unique `IX_CommitteeMembers_TenderId_UserId` (no person on both committees of a tender) is unchanged. |
| Document has exactly one owner | `ApplicationDbContext`: `ToTable(t => t.HasCheckConstraint("CK_Documents_ExactlyOneOwner", …))` | `CK_Documents_ExactlyOneOwner`: exactly one of `SupplierId`, `PurchaseRequestId`, `TenderId`, `BidId`, `ClarificationMessageId`, `TenderReportId` is not null |
| Clarification has a context | `ApplicationDbContext`: `ToTable(t => t.HasCheckConstraint("CK_Clarifications_HasContext", …))` | `CK_Clarifications_HasContext`: `PurchaseRequestId`, `TenderId` or `BidId` is not null |

**Verified afterwards (live SQL Server):** 30 tables (22 domain + 7 Identity + `__EFMigrationsHistory`);
4 migrations in `__EFMigrationsHistory`; 59 foreign keys (unchanged); 13 unique non-Identity indexes (12 + 1);
2 CHECK constraints, both trusted; `dotnet ef migrations has-pending-model-changes`: none; build succeeded.

Still application logic (not constraints): exactly four members per committee; all four Evaluation responsibilities
present; Top Committee members have no responsibility; the person in the `SourcingManager` seat actually holds the
`Sourcing Manager` role. No scoring method was added — `EvaluationCriterion.Weight`/`MaxScore` and
`CommitteeRound.TechnicalWeight`/`FinancialWeight` stay nullable for the committee's future rubric.
