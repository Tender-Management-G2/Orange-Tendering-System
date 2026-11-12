# CLAUDE.md — Orange Sourcing & Tender Management System

This file is the **primary working brief for Claude Code** when making changes in this repository. Read it before implementing features, designing tables, creating migrations, changing workflow logic, or generating UI. Consult linked documents for the full contract.

## Product in one paragraph

A seven-developer academic demo of Orange-inspired internal procurement, built with **ASP.NET Core MVC + C# + SQL Server + EF Core Code First + ASP.NET Core Identity**. Internal department users submit purchase requests directly to a Sourcing Officer. Officers create tenders, invite suppliers, collect **at least three offers from distinct invited suppliers**, run compliance checks, and send an initial comparison report to a **four-person Evaluation Committee**. The committee determines/approves configurable weights, scores eligible offers, and all four approve; the **system** calculates a supplier recommendation. That goes back to the Officer, then to the Sourcing Manager for report review and forwarding of a short summary, then to a **different four-person Top Committee**. Only **4/4 top approvals** can award a supplier. Reject/return/resubmit actions are simple but persisted/audited. Only suppliers self-register; Admin provisions internal users.

## Specification reading order

1. [`docs/DECISIONS_AND_SCOPE.md`](docs/DECISIONS_AND_SCOPE.md) — owner's authoritative clarifications, unresolved decisions, scope boundaries.
2. [`docs/WORKFLOW.md`](docs/WORKFLOW.md) — state transitions and role ownership.
3. [`docs/DATA_MODEL.md`](docs/DATA_MODEL.md) — entity model and relational constraints.
4. [`docs/EVALUATION_AND_APPROVALS.md`](docs/EVALUATION_AND_APPROVALS.md) — scoring, 4/4 votes, report versions.
5. [`docs/SECURITY_AND_REQUIREMENTS.md`](docs/SECURITY_AND_REQUIREMENTS.md) — access checks and mandatory advanced features.
6. [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) — workstream ownership, milestones and testing.
7. [`README.md`](README.md) — product/architecture overview and public project information.
8. Original assignment `.docx` — fill gaps **only when** it does not conflict with the owner's flow.

### Source-of-truth rule

**Explicit owner clarification > attached assignment brief > recorded implementation decision > agent inference.** Do not silently re-add approval stages, hardcode weights, make the Top Committee conditional on tender value, give the Manager an evaluation vote, or merge the two four-person committees. When an important decision is missing, design an extension point, update the decision log, and avoid inventing business policy.

## Hard business invariants

- A supplier must be Admin-approved, active and invited to see/submit a bid for a tender.
- Only **supplier** registration is public. Internal employee credentials and role/department assignments are Admin-managed.
- A Purchase Request reaches the Sourcing Officer directly. Officer may return/clarify it. A separate purchase approval stage is **not** mandatory.
- At least **3 different supplier companies** must have **submitted** bids before formal comparison/evaluation can advance. Three invited suppliers or three bid revisions from the same supplier do **not** count. Compliance exclusion is a separate gate.
- Committee membership is **tender-specific**: exactly 4 for Evaluation and exactly 4 distinct people for Top in the demo. Every vote must check role **and** membership **and** correct round.
- **Evaluation stage:** all 4 Approve the same finalized report/rubric/round. No 3/4, no majority, no implicit automatic approval. Reject leads to documented correction/resubmission path.
- Weights are **TBD**; `60/40` is illustrative. All scoring must be deterministic, explainable and derived from an explicitly finalized/versioned rubric. Never let Ollama decide the winner. Technical/financial scoring algorithm, ties and aggregation remain unresolved; do not encode guessed defaults as final policy.
- The system produces the **ranked recommendation**; award is a **separate** persisted outcome after 4/4 Top approvals.
- After evaluation approval, result goes back to **Sourcing Officer**, then to **Sourcing Manager**, then the **Top Committee sees a SHORT summary**.
- Sourcing Manager can edit managerial narrative/summary; **frozen numerical scores or committee votes cannot be overwritten**. Changed evaluation inputs require a new evaluation round.
- Top Committee 4/4 Approve of the same summary version is required. A rejection must remain visible; a materially revised summary requires a fresh round of four approvals.
- Supplier A must never access Supplier B's bids, documents, invitations, prices or evaluations. UI hiding is **not** authorization.
- Use data-driven reports. No placeholder statistics presented as real values.

## Implementation practices

- Inspect existing code, project structure, TargetFramework and migrations **before** generating classes. Preserve established naming/architecture where reasonable; do not recreate the solution.
- Keep controllers thin. Place validation, scoring, state transitions and authorization in testable application/domain services, not controllers/views/JavaScript.
- Use ASP.NET Core Identity for accounts. Use roles/policies PLUS **resource checks** (request ownership, tender invitation, committee assignment and report stage).
- Use EF Core Code First migrations for **SQL Server**; keep them reviewed, incremental and compatible with current schema. Prefer explicit fluent configurations, foreign keys, unique indexes, decimal precision, and `DeleteBehavior.Restrict` for decision history.
- Use `decimal` for prices/weights, `DateTimeOffset`/UTC for events, money currency codes, server-generated IDs, and concurrency checks on frequently edited entities. Validate ranges, bid deadlines and score totals on server.
- Persist bid **versions** and report **versions**; do not overwrite submitted bids or approved reports. Reopening after deadline must be authorized and audited.
- Save confidential documents outside `wwwroot`. Validate extension/MIME/signature/size, generate safe storage names, scan if available, and check document authorization at download time.
- Capture important actions in audit records: invitation, submission/withdrawal, compliance, rubric approval, scores, report generation, each vote, manager review, return, award, and access/security-sensitive errors.
- Use antiforgery protection, model validation, secure cookie configuration and no secrets in Git.
- Controllers/services should return useful validation/conflict errors rather than quietly altering statuses or skipping business guards.
- Write tests for each state gate, cross-supplier isolation, each 4/4 stage, duplicate votes, report revision invalidation and tie/no-winner handling.
- Do not invent currently nonexistent solution/project paths, connection-string names, secrets or working commands. Confirm against repo first.

## Mandatory versus deferred scope

**Required from the assignment:** robust biometric **face + fingerprint** login (no static photo as live face recognition), **Ollama** advisory bid analysis, **SpeechRecognition** task voice search, and **role-/stage-limited clarification** with status and replies. These must be planned and eventually implemented; they are **not optional**.

**Deferred options, NOT for initial build:** QR codes, digital signatures, custom download/view watermarks. The assignment says choose **at least two** eventually; document for later, but **do not implement them proactively**.

Biometric caution: WebAuthn/passkeys are useful but do not reveal to a website whether the user used a face or fingerprint; do not claim that two labeled buttons alone fulfill both modality requirements. A real, privacy-aware biometric feasibility decision and demonstration plan is needed. See security docs.

## Preferred task workflow for Claude

1. Determine which domain slice the user requested. Read its docs and the actual existing code.
2. Summarize the impacted entities/endpoints/status transitions/permissions and any open business rules.
3. Make the **smallest coherent change** that follows existing project patterns; preserve migrations and backward compatibility.
4. Add/update tests for **happy path + forbidden actor + invalid transition + stale revision + duplicate submission/vote**.
5. Build/test if tool access permits; report exactly what ran and what could not be verified.
6. Update docs/decision log if code behavior changes. Never claim a feature works solely because the UI exists.

## Recommended module boundaries

`IdentityAdmin`, `DepartmentsAndRequests`, `TendersAndInvitations`, `SupplierBids`, `Compliance`, `EvaluationAndRanking`, `CommitteeApprovals`, `ManagerAndExecutiveReports`, `ClarificationsAndNotifications`, `Analytics`, `BiometricAuth`, `OllamaAnalysis`, `VoiceSearch`.

Module names are conceptual, not prescribed project folder names. Implement ordinary MVC pages/controllers with application services; microservices are unnecessary for this assignment.

## Correctness examples

- **Wrong:** Three invitations exist → evaluation enabled. **Right:** Three distinct invited supplier companies have submitted bids → eligible for the three-offer gate.
- **Wrong:** Three Top Committee users approve → winner awarded. **Right:** Four recorded approvals of the **same round/report version**, then award.
- **Wrong:** Hardcode tech=60/financial=40. **Right:** store approved weights per evaluation version; initially unresolved.
- **Wrong:** AI recommendation directly selects/awards a bid. **Right:** deterministic ranking with independent advisory AI text.
- **Wrong:** Sourcing Manager modifies committee score rows. **Right:** return to new evaluation round, or edit summary narrative only.
- **Wrong:** Supplier can download a document via a guessed ID. **Right:** verify document ownership/invitation/scope on every access.
- **Wrong:** Add QR/watermarks now because the brief lists them. **Right:** deferred until explicitly scheduled.

## Delivery conventions

Communicate significant ambiguity before baking it into the database or public API. For reversible UI choices, use sensible project-consistent defaults. For missing **business rules** (especially scoring and biometric method) leave clear, actionable TODOs or implementation interfaces rather than false certainty. Keep all seeded supplier and user data synthetic for the demo. Do not use real Orange internal data.
