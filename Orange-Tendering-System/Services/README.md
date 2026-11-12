# Services

Business logic goes here: **Controller → Service → `ApplicationDbContext`**. Controllers stay thin; views never
contain rules. Register each service in `Program.cs` (`builder.Services.AddScoped<...>()`).

Planned services (create them when you implement the feature — nothing exists yet):

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
