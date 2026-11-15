# Services

Business logic goes here: **Controller → Service → `ApplicationDbContext`**. Controllers stay thin; views never
contain rules. Register each service in `Program.cs` (`builder.Services.AddScoped<...>()`).

Implemented services now include `TenderWorkflowService`, `WeightedCalculation`,
`ProtectedDocumentStore`, `OllamaBidAnalysisService`, and `EvaluationDeadlineWorker`.
Round control (re-tender limit, fewer-supplier approval, supplier carry-forward, scheduled opening) is in
`TenderWorkflowService.Rounds.cs`; `ZoomMeetingService`/`EvaluationSchedulingService` schedule evaluation meetings and
`ExternalInvitationDispatcher` delivers invitations to unregistered suppliers. See
[the round control delivery](../../docs/ROUND_CONTROL_DELIVERY.md).
See [the weighted workflow delivery](../../docs/WEIGHTED_WORKFLOW_DELIVERY.md) for the approved formulas,
policy gates, SQL Server test results and configuration. The table below retains the original module ownership
plan; suggested class names are not additional parallel implementations to create.

| Service | Owner | Responsibility |
|---|---|---|
| `PurchaseRequestService` | Doulat | Create, submit (snapshot), return/accept/reject |
| `TenderService` | Amal | Tenders, rounds, invitations, minimum-offer rule (3, or 2 after the allowed failed attempts) |
| `BidService` | Saja (+ Supplier portal owner) | Offer revisions, compliance, comparison totals, forward to committee |
| `EvaluationService` | Tariq | Criteria, scores, deterministic ranking (formula still undecided), committee votes |
| `OllamaAnalysisService` | Tariq | Advisory AI analysis only |
| `ReportService` | Haya (+ Officer reports owner) | Report versions, manager decisions, executive summary |
| `TopCommitteeService` | Doulat | Top committee rounds, 4/4 votes, award |
| `AdminService` | Rjoub | Users, roles, departments, supplier approval |
| `NotificationService`, `AuditService`, `ClarificationService` | ALL TEAM | Shared by every module |
| `DocumentStorageService` | ALL TEAM | Safe upload/download outside `wwwroot` |
