# Route Map

File paths are relative to the project folder `Orange-Tendering-System/`.

Generated from the scaffold manifest. Area routes use `{area:exists}/{controller=Dashboard}/{action=Index}/{id?}`;
public and shared pages use the default route or attribute routes. Every listed route is a GET that returns a view
unless noted.

**Authorization today:** area controllers use `[Authorize(Roles = SystemRoles.X)]`; shared pages use `[Authorize]`.
Role checks are not enough for the final system — tender-specific membership, record ownership and workflow stage
must also be checked when business logic is added.

| Route | Controller → action | View | Authorization |
|---|---|---|---|
| `/Admin` | Admin/DashboardController → Index | `Areas/Admin/Views/Dashboard/Index.cshtml` | Role: Admin |
| `/Admin/Users` | Admin/UsersController → Index | `Areas/Admin/Views/Users/Index.cshtml` | Role: Admin |
| `/Admin/Users/Create` | Admin/UsersController → Create | `Areas/Admin/Views/Users/Create.cshtml` | Role: Admin |
| `/Admin/Users/Edit/{id?}` | Admin/UsersController → Edit | `Areas/Admin/Views/Users/Edit.cshtml` | Role: Admin |
| `/Admin/Users/Details/{id?}` | Admin/UsersController → Details | `Areas/Admin/Views/Users/Details.cshtml` | Role: Admin |
| `/Admin/Roles` | Admin/RolesController → Index | `Areas/Admin/Views/Roles/Index.cshtml` | Role: Admin |
| `/Admin/Departments` | Admin/DepartmentsController → Index | `Areas/Admin/Views/Departments/Index.cshtml` | Role: Admin |
| `/Admin/Departments/Create` | Admin/DepartmentsController → Create | `Areas/Admin/Views/Departments/Create.cshtml` | Role: Admin |
| `/Admin/Departments/Edit/{id?}` | Admin/DepartmentsController → Edit | `Areas/Admin/Views/Departments/Edit.cshtml` | Role: Admin |
| `/Admin/Suppliers` | Admin/SuppliersController → Index | `Areas/Admin/Views/Suppliers/Index.cshtml` | Role: Admin |
| `/Admin/Suppliers/Review/{id?}` | Admin/SuppliersController → Review | `Areas/Admin/Views/Suppliers/Review.cshtml` | Role: Admin |
| `/Admin/Suppliers/Details/{id?}` | Admin/SuppliersController → Details | `Areas/Admin/Views/Suppliers/Details.cshtml` | Role: Admin |
| `/Admin/Settings` | Admin/SettingsController → Index | `Areas/Admin/Views/Settings/Index.cshtml` | Role: Admin |
| `/Admin/AuditLogs` | Admin/AuditLogsController → Index | `Areas/Admin/Views/AuditLogs/Index.cshtml` | Role: Admin |
| `/Department` | Department/DashboardController → Index | `Areas/Department/Views/Dashboard/Index.cshtml` | Role: Requesting Department |
| `/Department/PurchaseRequests` | Department/PurchaseRequestsController → Index | `Areas/Department/Views/PurchaseRequests/Index.cshtml` | Role: Requesting Department |
| `/Department/PurchaseRequests/Create` | Department/PurchaseRequestsController → Create | `Areas/Department/Views/PurchaseRequests/Create.cshtml` | Role: Requesting Department |
| `/Department/PurchaseRequests/Details/{id?}` | Department/PurchaseRequestsController → Details | `Areas/Department/Views/PurchaseRequests/Details.cshtml` | Role: Requesting Department |
| `/SourcingOfficer` | SourcingOfficer/DashboardController → Index | `Areas/SourcingOfficer/Views/Dashboard/Index.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/PurchaseRequests` | SourcingOfficer/PurchaseRequestsController → Index | `Areas/SourcingOfficer/Views/PurchaseRequests/Index.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/PurchaseRequests/Review/{id?}` | SourcingOfficer/PurchaseRequestsController → Review | `Areas/SourcingOfficer/Views/PurchaseRequests/Review.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Tenders` | SourcingOfficer/TendersController → Index | `Areas/SourcingOfficer/Views/Tenders/Index.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Tenders/Create/{id?}` | SourcingOfficer/TendersController → Create | `Areas/SourcingOfficer/Views/Tenders/Create.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Tenders/SelectSuppliers/{id?}` | SourcingOfficer/TendersController → SelectSuppliers | `Areas/SourcingOfficer/Views/Tenders/SelectSuppliers.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Tenders/Timeline/{id?}` | SourcingOfficer/TendersController → Timeline | `Areas/SourcingOfficer/Views/Tenders/Timeline.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Tenders/Rounds/{id?}` | SourcingOfficer/TendersController → Rounds | `Areas/SourcingOfficer/Views/Tenders/Rounds.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Invitations/Index/{id?}` | SourcingOfficer/InvitationsController → Index | `Areas/SourcingOfficer/Views/Invitations/Index.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Bids/Index/{id?}` | SourcingOfficer/BidsController → Index | `Areas/SourcingOfficer/Views/Bids/Index.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Bids/Details/{id?}` | SourcingOfficer/BidsController → Details | `Areas/SourcingOfficer/Views/Bids/Details.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Bids/Compliance/{id?}` | SourcingOfficer/BidsController → Compliance | `Areas/SourcingOfficer/Views/Bids/Compliance.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Bids/Compare/{id?}` | SourcingOfficer/BidsController → Compare | `Areas/SourcingOfficer/Views/Bids/Compare.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Bids/ForwardToCommittee/{id?}` | SourcingOfficer/BidsController → ForwardToCommittee | `Areas/SourcingOfficer/Views/Bids/ForwardToCommittee.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Bids/History/{id?}` | SourcingOfficer/BidsController → History | `Areas/SourcingOfficer/Views/Bids/History.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Clarifications` | SourcingOfficer/ClarificationsController → Index | `Areas/SourcingOfficer/Views/Clarifications/Index.cshtml` | Role: Sourcing Officer |
| `/SourcingOfficer/Reports` | SourcingOfficer/ReportsController → Index | `Areas/SourcingOfficer/Views/Reports/Index.cshtml` | Role: Sourcing Officer |
| `/Supplier` | Supplier/DashboardController → Index | `Areas/Supplier/Views/Dashboard/Index.cshtml` | Role: Supplier |
| `/Supplier/Profile` | Supplier/ProfileController → Index | `Areas/Supplier/Views/Profile/Index.cshtml` | Role: Supplier |
| `/Supplier/Invitations` | Supplier/InvitationsController → Index | `Areas/Supplier/Views/Invitations/Index.cshtml` | Role: Supplier |
| `/Supplier/Invitations/Details/{id?}` | Supplier/InvitationsController → Details | `Areas/Supplier/Views/Invitations/Details.cshtml` | Role: Supplier |
| `/Supplier/Invitations/Respond/{id?}` | Supplier/InvitationsController → Respond | `Areas/Supplier/Views/Invitations/Respond.cshtml` | Role: Supplier |
| `/Supplier/Offers` | Supplier/OffersController → Index | `Areas/Supplier/Views/Offers/Index.cshtml` | Role: Supplier |
| `/Supplier/Offers/Create/{id?}` | Supplier/OffersController → Create | `Areas/Supplier/Views/Offers/Create.cshtml` | Role: Supplier |
| `/Supplier/Offers/Edit/{id?}` | Supplier/OffersController → Edit | `Areas/Supplier/Views/Offers/Edit.cshtml` | Role: Supplier |
| `/Supplier/Offers/Details/{id?}` | Supplier/OffersController → Details | `Areas/Supplier/Views/Offers/Details.cshtml` | Role: Supplier |
| `/Supplier/Offers/History/{id?}` | Supplier/OffersController → History | `Areas/Supplier/Views/Offers/History.cshtml` | Role: Supplier |
| `/Supplier/Documents` | Supplier/DocumentsController → Index | `Areas/Supplier/Views/Documents/Index.cshtml` | Role: Supplier |
| `/Supplier/Clarifications` | Supplier/ClarificationsController → Index | `Areas/Supplier/Views/Clarifications/Index.cshtml` | Role: Supplier |
| `/EvaluationCommittee` | EvaluationCommittee/DashboardController → Index | `Areas/EvaluationCommittee/Views/Dashboard/Index.cshtml` | Role: Evaluation Committee |
| `/EvaluationCommittee/Evaluations` | EvaluationCommittee/EvaluationsController → Index | `Areas/EvaluationCommittee/Views/Evaluations/Index.cshtml` | Role: Evaluation Committee |
| `/EvaluationCommittee/Criteria/Index/{id?}` | EvaluationCommittee/CriteriaController → Index | `Areas/EvaluationCommittee/Views/Criteria/Index.cshtml` | Role: Evaluation Committee |
| `/EvaluationCommittee/Evaluations/Technical/{id?}` | EvaluationCommittee/EvaluationsController → Technical | `Areas/EvaluationCommittee/Views/Evaluations/Technical.cshtml` | Role: Evaluation Committee |
| `/EvaluationCommittee/Evaluations/Financial/{id?}` | EvaluationCommittee/EvaluationsController → Financial | `Areas/EvaluationCommittee/Views/Evaluations/Financial.cshtml` | Role: Evaluation Committee |
| `/EvaluationCommittee/Evaluations/Scores/{id?}` | EvaluationCommittee/EvaluationsController → Scores | `Areas/EvaluationCommittee/Views/Evaluations/Scores.cshtml` | Role: Evaluation Committee |
| `/EvaluationCommittee/Evaluations/AiAnalysis/{id?}` | EvaluationCommittee/EvaluationsController → AiAnalysis | `Areas/EvaluationCommittee/Views/Evaluations/AiAnalysis.cshtml` | Role: Evaluation Committee |
| `/EvaluationCommittee/Evaluations/History/{id?}` | EvaluationCommittee/EvaluationsController → History | `Areas/EvaluationCommittee/Views/Evaluations/History.cshtml` | Role: Evaluation Committee |
| `/EvaluationCommittee/Reports/Unified/{id?}` | EvaluationCommittee/ReportsController → Unified | `Areas/EvaluationCommittee/Views/Reports/Unified.cshtml` | Role: Evaluation Committee |
| `/SourcingManager` | SourcingManager/DashboardController → Index | `Areas/SourcingManager/Views/Dashboard/Index.cshtml` | Role: Sourcing Manager |
| `/SourcingManager/Reports` | SourcingManager/ReportsController → Index | `Areas/SourcingManager/Views/Reports/Index.cshtml` | Role: Sourcing Manager |
| `/SourcingManager/Reports/Review/{id?}` | SourcingManager/ReportsController → Review | `Areas/SourcingManager/Views/Reports/Review.cshtml` | Role: Sourcing Manager |
| `/SourcingManager/Reports/Approve/{id?}` | SourcingManager/ReportsController → Approve | `Areas/SourcingManager/Views/Reports/Approve.cshtml` | Role: Sourcing Manager |
| `/SourcingManager/Reports/Reject/{id?}` | SourcingManager/ReportsController → Reject | `Areas/SourcingManager/Views/Reports/Reject.cshtml` | Role: Sourcing Manager |
| `/SourcingManager/Reports/Return/{id?}` | SourcingManager/ReportsController → Return | `Areas/SourcingManager/Views/Reports/Return.cshtml` | Role: Sourcing Manager |
| `/SourcingManager/Reports/Forward/{id?}` | SourcingManager/ReportsController → Forward | `Areas/SourcingManager/Views/Reports/Forward.cshtml` | Role: Sourcing Manager |
| `/SourcingManager/Reports/History` | SourcingManager/ReportsController → History | `Areas/SourcingManager/Views/Reports/History.cshtml` | Role: Sourcing Manager |
| `/TopCommittee` | TopCommittee/DashboardController → Index | `Areas/TopCommittee/Views/Dashboard/Index.cshtml` | Role: Top Committee |
| `/TopCommittee/Decisions/ReviewEvaluation/{id?}` | TopCommittee/DecisionsController → ReviewEvaluation | `Areas/TopCommittee/Views/Decisions/ReviewEvaluation.cshtml` | Role: Top Committee |
| `/TopCommittee/Decisions/Decision/{id?}` | TopCommittee/DecisionsController → Decision | `Areas/TopCommittee/Views/Decisions/Decision.cshtml` | Role: Top Committee |
| `/TopCommittee/Decisions/History/{id?}` | TopCommittee/DecisionsController → History | `Areas/TopCommittee/Views/Decisions/History.cshtml` | Role: Top Committee |
| `/TopCommittee/Statistics` | TopCommittee/StatisticsController → Index | `Areas/TopCommittee/Views/Statistics/Index.cshtml` | Role: Top Committee |
| `/Workspace` | WorkspaceController → Index | `Views/Workspace/Index.cshtml` | Any signed-in user ([Authorize]) |
| `/Biometrics/Fingerprint` | BiometricsController → Fingerprint | `Views/Biometrics/Fingerprint.cshtml` | Any signed-in user ([Authorize]) |
| `/Biometrics/Face` | BiometricsController → Face | `Views/Biometrics/Face.cshtml` | Any signed-in user ([Authorize]) |
| `/Tasks` | TasksController → Index | `Views/Tasks/Index.cshtml` | Any signed-in user ([Authorize]) |
| `/Notifications` | NotificationsController → Index | `Views/Notifications/Index.cshtml` | Any signed-in user ([Authorize]) |
| `/Clarifications` | ClarificationsController → Index | `Views/Clarifications/Index.cshtml` | Any signed-in user ([Authorize]) |
| `/Clarifications/Details/{id?}` | ClarificationsController → Details | `Views/Clarifications/Details.cshtml` | Any signed-in user ([Authorize]) |
| `/` | Home → Index | `Views/Home/Index.cshtml` | Anonymous |
| `/about` | Home → About | `Views/Home/About.cshtml` | Anonymous |
| `/how-it-works` | Home → HowItWorks | `Views/Home/HowItWorks.cshtml` | Anonymous |
| `/team` | Home → Team | `Views/Home/Team.cshtml` | Anonymous |
| `/contact` | Home → Contact | `Views/Home/Contact.cshtml` | Anonymous |
| `/Home/Privacy` | Home → Privacy | `Views/Home/Privacy.cshtml` | Anonymous |
| `/Home/Error` | Home → Error | `Views/Shared/Error.cshtml` | Anonymous |
| `/status/{code}` | Home → HttpStatus | `Views/Home/HttpStatus.cshtml` | Anonymous |
| `POST /contact/submit` | Contact → Submit (POST) | `— (JSON 501)` | Anonymous (antiforgery token) |
| `/Identity/Account/Register` | Identity: Account/Register → — | `Areas/Identity/Pages/Account/Register.cshtml` | Anonymous |
| `/Identity/Account/Login` | Identity: Account/Login → — | `Areas/Identity/Pages/Account/Login.cshtml` | Anonymous |
| `/Identity/Account/ForgotPassword` | Identity: Account/ForgotPassword → — | `Areas/Identity/Pages/Account/ForgotPassword.cshtml` | Anonymous |
| `/Identity/Account/ResetPassword` | Identity: Account/ResetPassword → — | `Areas/Identity/Pages/Account/ResetPassword.cshtml` | Anonymous (reset code) |
| `/Identity/Account/AccessDenied` | Identity: Account/AccessDenied → — | `Areas/Identity/Pages/Account/AccessDenied.cshtml` | Signed-in user |
| `/Identity/Account/Manage, Logout, ConfirmEmail, …` | Identity (default UI) → — | `Microsoft.AspNetCore.Identity.UI (not scaffolded)` | Varies |

## End-to-end route checklist (ALL TEAM — End-to-End Workflow Testing)

Walk the approved workflow once per demo, with one account per role, and tick each step only when it works
on real data:

1. Supplier registers → `/Identity/Account/Register`; Admin approves → `/Admin/Suppliers/Review/{id}`.
2. Department creates and submits a request → `/Department/PurchaseRequests/Create`.
3. Officer reviews it → `/SourcingOfficer/PurchaseRequests/Review/{id}` (return once, then accept).
4. Officer creates the tender and round 1 → `/SourcingOfficer/Tenders/Create`, invites suppliers → `/SourcingOfficer/Tenders/SelectSuppliers/{id}`.
5. Suppliers accept and submit offers → `/Supplier/Invitations/Respond/{id}`, `/Supplier/Offers/Create/{id}`.
6. Fewer than 3 offers → new round → `/SourcingOfficer/Tenders/Rounds/{id}`.
7. Officer checks compliance and compares → `/SourcingOfficer/Bids/Compliance/{id}`, `/SourcingOfficer/Bids/Compare/{id}`.
8. Officer forwards to the committee → `/SourcingOfficer/Bids/ForwardToCommittee/{id}`.
9. Committee sets criteria, scores, approves 4/4 → `/EvaluationCommittee/Criteria/Index/{id}`, `…/Evaluations/Technical`, `…/Financial`, `…/Reports/Unified`.
10. Officer submits the full report → `/SourcingOfficer/Reports`; Manager reviews, returns once, forwards → `/SourcingManager/Reports/Review/{id}`, `…/Forward/{id}`.
11. Top Committee votes 4/4 → `/TopCommittee/Decisions/Decision/{id}`; also test one rejection.
12. Check `/Notifications`, `/Clarifications` and `/Admin/AuditLogs` show every step.
13. Negative checks: wrong role → Access Denied; Supplier A cannot open Supplier B's invitation or offer by id.
