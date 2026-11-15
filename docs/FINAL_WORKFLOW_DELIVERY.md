> Historical pre-integration delivery record. For the current branch, configuration, verification and removal of the workflow test project, see [INTEGRATION_DELIVERY.md](INTEGRATION_DELIVERY.md). Statements about earlier application-database migrations describe that earlier work, not this integration.

# Final Orange tender cycle — verified delivery

13 November 2026. This supersedes earlier verification/status sections in the weighted, report-lifecycle and round-control handoff documents. Existing completed components were reused.

## Verified results

- Real .NET 10 solution build: **succeeded, 0 warnings, 0 errors**.
- Current xUnit suite: **29 passed, 0 failed, 0 skipped** (9 calculation/configuration guards and 20 SQL Server integration tests).
- SQL tests use the real SQL Server provider, Identity stores, migrations and services in randomly named `OrangeTenderingSystem_Test_*` databases. No SQLite or EF InMemory is referenced. Production data was not used as test fixtures.
- Structured results: `docs/test-results/final-cycle.trx`; readable results: `docs/FINAL_WORKFLOW_TEST_RESULTS.txt`.
- EF reports no pending model changes.
- The real application started against `localhost / OrangeTenderingSystem`. Public home and supplier registration returned 200. The registration page rendered company details, document upload and an antiforgery token. Officer, evaluator, weight approval, Manager, Top report and Admin supplier routes redirected anonymous users to login (302).
- The local Ollama server listed `gemma3:4b`. A real `/api/chat` connectivity-only prompt returned `model=gemma3:4b`, `done=true`, and valid JSON `{"ready":true}`. No tender/personal data was sent in that smoke check. The SQL-backed AI service test separately verifies actual stored bid evidence, the chat request/response contract and failure isolation using a substituted HTTP transport.
- Both pending migrations were applied successfully to the existing application database: `20261113110000_RoundControlAndScheduling` and `20261113111426_FinalizeTenderCycle`. No existing records were deleted/reset or seeded. Read-only counts in that database: 0 users, 0 tenders, 0 bids, 0 suppliers.
- The temporary HTTP verification server was stopped after checking routes.

## Completed cycle

1. Exactly one active Identity user in the Sourcing Officer role manages **all** tenders, including records created by historical users. CreatedBy remains historical audit information, not an ownership restriction. Zero or multiple active Officers fail closed for management operations. No fake Officer account is seeded.
2. The Officer freezes tender requirements and sends four persisted weight requests. Each assigned evaluator proposes once, percentages total 100 and four proposals produce the exact decimal mean. Only the **assigned Sourcing Manager** can approve the average, even if another user has the same configured Manager role. Approved weights are locked before bidding.
3. The Officer opens bidding and invites approved registered suppliers. After every closed round the UI offers a real persisted **Proceed** decision or Re-Tender. Retendering is available regardless of received offer count before evaluation setup, with a mandatory reason, at most three retries/four rounds and fresh deadlines. A fifth round is blocked.
4. Existing offer-minimum and documented proceed-exception safeguards are retained. Proceed does not bypass compliance or other approvals. Prior supplier registrations, invitations, documents and bids remain intact; no old bid is copied into a later round. Previously invited, still-approved suppliers receive new round invitations and notifications automatically.
5. The existing SQL invitation queue now distinguishes registered suppliers from new contacts. In-app notifications are immediate. Registered-supplier emails and new-company registration invitations use the configured external channel. Unconfigured/failed delivery is persisted honestly. SQL locks prevent concurrent worker sends; SMTP remains at-least-once across a crash after remote acceptance, not falsely exactly-once.
6. The existing public registration link now creates a **Pending company**, linked Supplier Identity account and private commercial-registration document transactionally. It cannot create employee roles or approve itself. Account email confirmation uses the configured delivery channel/public HTTPS origin. Admin has real review/download/approve/reject pages. An approved company matching a previously invited contact receives its current-round tender invitation once; registration alone grants no tender access.
7. Complete compliance screening precedes Ollama analysis. The AI sample ZIP's `/api/chat`, message-role and `message.content` response pattern was reused in the existing analysis service with `gemma3:4b`; no duplicate chat module was created. Actual submitted bid values and safely extracted evidence are used. AI remains supplementary and cannot edit official weights, scores, approvals or award. Repeated requests no longer try to reset an already-used HttpClient's timeout.
8. The Officer reviews the AI record and schedules evaluation with the supplied HTTPS Zoom link or configured server-to-server Zoom integration. The four persisted invitations link to the tender's meeting, eligible bid documents and reviewed AI report. The scheduling service validates first; if saving fails after remote meeting creation it attempts cleanup. A manually supplied link is labelled as such.
9. Scoring authorization opens at the server start time and closes at the deadline, independent of JavaScript. The worker records openings and sends notifications. Each assigned member submits both scores, observations and the entire eligible supplier set atomically, once.
10. **End Evaluation** is available for four complete submissions, requires confirmation/reason, closes scoring and automatically computes member scores, final supplier means, exact ranking and the Orange PDF in the same transaction. Repeated/concurrent closure cannot create duplicate reports. Complete deadline closure generates the report too; incomplete deadline closure becomes Incomplete with no report/recommendation. Manual incomplete finalization is blocked.
11. The existing automatic PDF layout, fixed signature locations, version hashes, protected personalized watermarks and electronic-acknowledgment receipts are preserved. Every committee member opens/reviews the current PDF and explicitly signs their assigned section. All four current-version signatures are required before Officer forwarding.
12. The assigned Manager reviews/approves/rejects/returns and signs separately from their evaluation seat. When configured as required, four separate Top members vote/sign; **3 approvals out of 4** suffice under the configured policy (quorum 3, approvals 3, a dissent does not veto). Two approvals cannot award. No award occurs without the applicable current-version committee/Manager/Top approval set.
13. Controlled narrative returns preserve mandatory feedback, responsibility, report history, original scores, old receipts and decisions. New versions recalculate from preserved authorized inputs and require fresh signatures. Undefined revision routes or attempts to unlock approved weights, deadline-locked bids or finalized scores stay policy-blocked. Existing Top-scope policy is preserved (`All` in current appsettings); no new high-value threshold or currency conversion was invented.

Formulas are unchanged: proposal average = four proposals / 4; member score = (financial score x approved financial weight + technical score x approved technical weight) / 100; final supplier score = all four member results / 4. Official C# decimal calculations do not round intermediate results. Exact highest-score supplier ties remain blocked pending a documented resolution policy.

## ZIP inspection

- `AIChatAssistant.zip`: contains a general MVC chat sample calling Ollama `/api/chat`. The useful protocol pattern was adapted into `OllamaBidAnalysisService`. Its MVC-help prompt, hardcoded `gemma3-cpu:latest`, UI and build artifacts were not copied into procurement operations.
- `meetingZoom-20260926T174353Z-1-001.zip`: contains HTML/CSS and `JitsiMeetExternalAPI` using `meet.jit.si`, not Zoom API code or credentials. It was inspected; reusing it as Zoom would be inaccurate. The existing actual Zoom service and supplied-link option were retained.

## Applied schema

RoundControlAndScheduling adds the existing prepared RoundDecision/ExternalSupplierInvitation tables and EvaluationSession scheduling metadata. FinalizeTenderCycle only adds nullable `ExternalSupplierInvitation.SupplierId`, its index and restrictive foreign key; registered and unregistered deliveries share the same queue. No additional reporting, scoring, supplier or award tables were duplicated. `docs/final-cycle-migration.sql` is an idempotent deployment script from the previous report-lifecycle baseline.

## Verified test coverage

The current suite covers exact averages/formulas, missing scores, ties, four-round limit, bid/history preservation, re-tender after sufficient offers, pending exception withdrawal, suspension eligibility, external delivery configuration/queue handling, documented proceed exceptions, scheduled opening/closing, Zoom request/precheck handling, independent scoring, the complete signed PDF → Manager return → new version/fresh signatures → Manager approval → 3-of-4 Top → award cycle, global Officer scope, duplicate Officer fail-closed behavior, assigned Manager approval, concurrent/idempotent closure, complete automatic deadline PDF generation, explicit Proceed, registration approval/linking, Ollama protocol/failure isolation, PDF tampering, concurrent signatures, explicit consent/current hash, duplicate score prevention and competitor document protection.

The Zoom HTTP handler, external delivery channel and AI transport in tests are substituted deliberately; their SQL persistence and business operations are real. Those tests do **not** prove live remote Zoom creation, SMTP delivery or the correctness of AI procurement conclusions.

## Remaining operational blockers

1. **Real accounts/master data:** the selected application database is empty. Provision the single active Officer, real departmental/request records, four assigned evaluators, approved companies and separate Top members through authorized processes. No production demonstration users/data were fabricated.
2. **Zoom API credentials:** AccountId, ClientId, ClientSecret and HostUserId are unset. Automatic remote meeting creation was not live-tested. A real supplied Zoom HTTPS link works without API credentials, and the UI states that it did not create the meeting.
3. **SMTP delivery:** ExternalInvitations is disabled and SMTP credentials/public HTTPS origin are unset. Real email/registration confirmation delivery was not sent or claimed. Configure them before inviting new companies externally. In-app notifications and queued delivery states are persisted.
4. **Undefined exceptional changes:** narrative Manager-to-Officer correction is defined; other return delegations require an explicitly approved route. Weight amendments, renewed final scoring, late bid changes and exact supplier-score tie resolution remain fail closed.
5. **Signing:** the requested authenticated electronic signing is implemented. Optional certificate-backed CMS mode still requires actual trusted personal signing credentials and production PKI verification; it is not represented as a completed embedded PDF/PAdES signature.
6. **Human browser verification:** Razor compiled, public/anonymous HTTP routes were checked, and role/business scope was tested through real Identity/SQL services. A real signed-in organizational user acceptance session was not performed because the application database has no accounts.

Live Ollama connectivity/model availability is verified; actual company bid-analysis quality still requires human review on genuine tender evidence.

## Run

From repository root:

```powershell
dotnet restore
dotnet build --no-restore -m:1
dotnet ef database update --project Orange-Tendering-System
dotnet test --no-restore -m:1 --logger "trx;LogFileName=final-cycle.trx" --results-directory docs/test-results
dotnet run --project Orange-Tendering-System --launch-profile https
```

Tests require SQL Server create/drop permission for isolated test databases. Optional `ORANGE_TEST_SQLSERVER` supplies the server/authentication connection; the test harness always replaces its database with a guarded random test name. It never runs fixtures in OrangeTenderingSystem.

Configure secrets through deployment/user secrets, never tracked source or chat. Keep the existing seven role-specific layouts. Enter at `/SourcingOfficer/Workflow`; assigned Managers can approve weights from their tender page, and generated report review/signing stays under each role's Reports Area.

## Files changed in this finalization

- Services: TenderWorkflowService (main/Rounds/Reports), OllamaBidAnalysisService, ExternalInvitationDispatcher, EvaluationDeadlineWorker, ProtectedDocumentStore; new SupplierRegistrationService and ConfiguredIdentityEmailSender.
- Areas: Officer Proceed controller action; existing role Workflow detail views and shared _RoundControls; Identity Register page/model; Admin Suppliers controller/review/list pages.
- Persistence: optional supplier link on RoundControl entity, FinalizeTenderCycle migration/Designer and updated model snapshot; pending RoundControlAndScheduling migration applied.
- Program.cs dependency registrations; current configuration retains gemma3:4b and the 3-of-4 Top policy.
- Tests: current xUnit fixtures/scenarios, RoundControlTests/SchedulingAndLifecycleTests updates, new FinalCycleTests and SupplierOnboardingTests.
- This verified delivery record, readable/TRX results and idempotent deployment SQL.
