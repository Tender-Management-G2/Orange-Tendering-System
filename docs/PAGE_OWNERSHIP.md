# Page Ownership

Generated from the scaffold manifest. **Developer owner** = who builds it. **Business role** = who may use it.
Trello URLs were not available in the repository; only the one given in the task brief is filled in.
File paths are relative to the project folder `Orange-Tendering-System/`.

## 1. Trello card coverage (60 cards)

| # | Developer | Trello card | Trello URL | Implemented by (view or source) |
|---|---|---|---|---|
| 1 | Doulat | Department Dashboard | Not provided — add from the Trello board | `Areas/Department/Views/Dashboard/Index.cshtml` |
| 2 | Doulat | Create Purchase Request | Not provided — add from the Trello board | `Areas/Department/Views/PurchaseRequests/Create.cshtml` |
| 3 | Doulat | My Purchase Requests | Not provided — add from the Trello board | `Areas/Department/Views/PurchaseRequests/Index.cshtml` |
| 4 | Doulat | Purchase Request Details & History | Not provided — add from the Trello board | `Areas/Department/Views/PurchaseRequests/Details.cshtml` |
| 5 | Doulat | Purchase Request Review / Approval | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/PurchaseRequests/Review.cshtml` |
| 6 | Doulat | Top Committee Dashboard | Not provided — add from the Trello board | `Areas/TopCommittee/Views/Dashboard/Index.cshtml` |
| 7 | Doulat | Review Tender Evaluation | Not provided — add from the Trello board | `Areas/TopCommittee/Views/Decisions/ReviewEvaluation.cshtml` |
| 8 | Doulat | Committee Decision | Not provided — add from the Trello board | `Areas/TopCommittee/Views/Decisions/Decision.cshtml` |
| 9 | Doulat | Committee Voting & Decision History | Not provided — add from the Trello board | `Areas/TopCommittee/Views/Decisions/History.cshtml` |
| 10 | Haya | Sourcing Manager Dashboard | Not provided — add from the Trello board | `Areas/SourcingManager/Views/Dashboard/Index.cshtml` |
| 11 | Haya | Evaluation Reports List | Not provided — add from the Trello board | `Areas/SourcingManager/Views/Reports/Index.cshtml` |
| 12 | Haya | Review Unified Evaluation Report | Not provided — add from the Trello board | `Areas/SourcingManager/Views/Reports/Review.cshtml` |
| 13 | Haya | Approve Evaluation Report | Not provided — add from the Trello board | `Areas/SourcingManager/Views/Reports/Approve.cshtml` |
| 14 | Haya | Reject Evaluation Report | Not provided — add from the Trello board | `Areas/SourcingManager/Views/Reports/Reject.cshtml` |
| 15 | Haya | Return Report for Review | Not provided — add from the Trello board | `Areas/SourcingManager/Views/Reports/Return.cshtml` |
| 16 | Haya | Forward to Top Committee | Not provided — add from the Trello board | `Areas/SourcingManager/Views/Reports/Forward.cshtml` |
| 17 | Haya | Sourcing Manager Decision History | Not provided — add from the Trello board | `Areas/SourcingManager/Views/Reports/History.cshtml` |
| 18 | Ahmad | User Registration | Not provided — add from the Trello board | `Areas/Identity/Pages/Account/Register.cshtml` |
| 19 | Ahmad | User Login & Role Redirect | Not provided — add from the Trello board | `Views/Workspace/Index.cshtml`<br>`Areas/Identity/Pages/Account/Login.cshtml`<br>`Areas/Identity/Pages/Account/AccessDenied.cshtml` |
| 20 | Ahmad | Forgot / Reset Password | Not provided — add from the Trello board | `Areas/Identity/Pages/Account/ForgotPassword.cshtml`<br>`Areas/Identity/Pages/Account/ResetPassword.cshtml` |
| 21 | Ahmad | Fingerprint Registration & Login | Not provided — add from the Trello board | `Views/Biometrics/Fingerprint.cshtml` |
| 22 | Ahmad | Face Registration & Recognition Login | Not provided — add from the Trello board | `Views/Biometrics/Face.cshtml` |
| 23 | Ahmad | Database Design & ERD | Not provided — add from the Trello board | Models/Entities/*.cs, Data/ApplicationDbContext.cs |
| 24 | Ahmad | Database Implementation & Seed Data | Not provided — add from the Trello board | Data/Migrations/*, Models/SystemRoles.cs (role seeding TODO) |
| 25 | Ahmad | GitHub Repository & Team Workflow | Not provided — add from the Trello board | Repository, .gitignore, docs/TEAM_HANDOFF.md |
| 26 | Amal | Sourcing Officer Dashboard | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Dashboard/Index.cshtml` |
| 27 | Amal | Approved Purchase Requests | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/PurchaseRequests/Index.cshtml` |
| 28 | Amal | Create Tender | https://trello.com/c/907F6ufP | `Areas/SourcingOfficer/Views/Tenders/Create.cshtml` |
| 29 | Amal | Manage Tenders | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Tenders/Index.cshtml` |
| 30 | Amal | Evaluation Criteria & Weights | Not provided — add from the Trello board | `Areas/EvaluationCommittee/Views/Criteria/Index.cshtml` |
| 31 | Amal | Supplier Selection | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Tenders/SelectSuppliers.cshtml` |
| 32 | Amal | Supplier Invitations | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Invitations/Index.cshtml` |
| 33 | Amal | Tender Timeline & Closing | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Tenders/Timeline.cshtml` |
| 34 | Saja | Received Bids | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Bids/Index.cshtml` |
| 35 | Saja | Bid Details & Documents | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Bids/Details.cshtml` |
| 36 | Saja | Bid Compliance Check | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Bids/Compliance.cshtml` |
| 37 | Saja | Supplier Clarification Requests | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Clarifications/Index.cshtml` |
| 38 | Saja | Bid Comparison | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Bids/Compare.cshtml` |
| 39 | Saja | Forward Bids to Evaluation Committee | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Bids/ForwardToCommittee.cshtml` |
| 40 | Saja | Bid & Clarification History | Not provided — add from the Trello board | `Areas/SourcingOfficer/Views/Bids/History.cshtml` |
| 41 | Tariq | Home / Landing Page | Not provided — add from the Trello board | `Views/Home/Index.cshtml` |
| 42 | Tariq | Team Page | Not provided — add from the Trello board | `Views/Home/Team.cshtml` |
| 43 | Tariq | Shared Layout & Responsive Design | Not provided — add from the Trello board | Views/Shared/_Layout, _WorkspaceLayout, _SupplierLayout, _Sidebar, _PageHeader, …; wwwroot/css, wwwroot/js |
| 44 | Tariq | Committee Dashboard | Not provided — add from the Trello board | `Areas/EvaluationCommittee/Views/Dashboard/Index.cshtml` |
| 45 | Tariq | Technical Evaluation | Not provided — add from the Trello board | `Areas/EvaluationCommittee/Views/Evaluations/Technical.cshtml` |
| 46 | Tariq | Financial Evaluation | Not provided — add from the Trello board | `Areas/EvaluationCommittee/Views/Evaluations/Financial.cshtml` |
| 47 | Tariq | Committee Scoring & Consolidation | Not provided — add from the Trello board | `Areas/EvaluationCommittee/Views/Evaluations/Scores.cshtml` |
| 48 | Tariq | Ollama AI Bid Analysis | Not provided — add from the Trello board | `Areas/EvaluationCommittee/Views/Evaluations/AiAnalysis.cshtml` |
| 49 | Tariq | Unified Evaluation Report | Not provided — add from the Trello board | `Areas/EvaluationCommittee/Views/Reports/Unified.cshtml` |
| 50 | Tariq | Evaluation History | Not provided — add from the Trello board | `Areas/EvaluationCommittee/Views/Evaluations/History.cshtml` |
| 51 | Rjoub | Admin Dashboard | Not provided — add from the Trello board | `Areas/Admin/Views/Dashboard/Index.cshtml` |
| 52 | Rjoub | User Management | Not provided — add from the Trello board | `Areas/Admin/Views/Users/Index.cshtml`<br>`Areas/Admin/Views/Users/Create.cshtml`<br>`Areas/Admin/Views/Users/Edit.cshtml`<br>`Areas/Admin/Views/Users/Details.cshtml` |
| 53 | Rjoub | Role & Permission Management | Not provided — add from the Trello board | `Areas/Admin/Views/Roles/Index.cshtml` |
| 54 | Rjoub | Department Management | Not provided — add from the Trello board | `Areas/Admin/Views/Departments/Index.cshtml`<br>`Areas/Admin/Views/Departments/Create.cshtml`<br>`Areas/Admin/Views/Departments/Edit.cshtml` |
| 55 | Rjoub | Supplier Administration | Not provided — add from the Trello board | `Areas/Admin/Views/Suppliers/Index.cshtml`<br>`Areas/Admin/Views/Suppliers/Review.cshtml`<br>`Areas/Admin/Views/Suppliers/Details.cshtml` |
| 56 | Rjoub | System Settings | Not provided — add from the Trello board | `Areas/Admin/Views/Settings/Index.cshtml` |
| 57 | Rjoub | Audit Logs | Not provided — add from the Trello board | `Areas/Admin/Views/AuditLogs/Index.cshtml` |
| 58 | ALL TEAM | End-to-End Workflow Testing | Not provided — add from the Trello board | docs/ROUTE_MAP.md (end-to-end checklist), docs/TEAM_HANDOFF.md (testing) |
| 59 | ALL TEAM | Clarifications, Notifications & Audit Trail | Not provided — add from the Trello board | `Views/Notifications/Index.cshtml`<br>`Views/Clarifications/Index.cshtml`<br>`Views/Clarifications/Details.cshtml`<br>AuditLogs table; Admin/AuditLogs page; audit TODOs in every page |
| 60 | ALL TEAM | Final Bug Fixing & Demo Preparation | Not provided — add from the Trello board | `Views/Shared/Error.cshtml`<br>`Views/Home/HttpStatus.cshtml`<br>docs/SCAFFOLD_STATUS.md |

## 2. Every page

| Developer owner | Trello card | Module | Controller | Action | Razor view | Business role | Status |
|---|---|---|---|---|---|---|---|
| Rjoub | Admin Dashboard | Administration | Admin/DashboardController | Index | `Areas/Admin/Views/Dashboard/Index.cshtml` | Admin | Dashboard layout from ZIP; data not connected |
| Rjoub | User Management | Administration | Admin/UsersController | Index | `Areas/Admin/Views/Users/Index.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | User Management | Administration | Admin/UsersController | Create | `Areas/Admin/Views/Users/Create.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | User Management | Administration | Admin/UsersController | Edit | `Areas/Admin/Views/Users/Edit.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | User Management | Administration | Admin/UsersController | Details | `Areas/Admin/Views/Users/Details.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | Role & Permission Management | Administration | Admin/RolesController | Index | `Areas/Admin/Views/Roles/Index.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | Department Management | Administration | Admin/DepartmentsController | Index | `Areas/Admin/Views/Departments/Index.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | Department Management | Administration | Admin/DepartmentsController | Create | `Areas/Admin/Views/Departments/Create.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | Department Management | Administration | Admin/DepartmentsController | Edit | `Areas/Admin/Views/Departments/Edit.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | Supplier Administration | Administration | Admin/SuppliersController | Index | `Areas/Admin/Views/Suppliers/Index.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | Supplier Administration | Administration | Admin/SuppliersController | Review | `Areas/Admin/Views/Suppliers/Review.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | Supplier Administration | Administration | Admin/SuppliersController | Details | `Areas/Admin/Views/Suppliers/Details.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | System Settings | Administration | Admin/SettingsController | Index | `Areas/Admin/Views/Settings/Index.cshtml` | Admin | UI scaffold; logic not implemented |
| Rjoub | Audit Logs | Administration | Admin/AuditLogsController | Index | `Areas/Admin/Views/AuditLogs/Index.cshtml` | Admin | UI scaffold; logic not implemented |
| Doulat | Department Dashboard | Department / Purchase Requests | Department/DashboardController | Index | `Areas/Department/Views/Dashboard/Index.cshtml` | Requesting Department (own department only) | Dashboard layout from ZIP; data not connected |
| Doulat | My Purchase Requests | Department / Purchase Requests | Department/PurchaseRequestsController | Index | `Areas/Department/Views/PurchaseRequests/Index.cshtml` | Requesting Department (own department only) | UI scaffold; logic not implemented |
| Doulat | Create Purchase Request | Department / Purchase Requests | Department/PurchaseRequestsController | Create | `Areas/Department/Views/PurchaseRequests/Create.cshtml` | Requesting Department | UI scaffold; logic not implemented |
| Doulat | Purchase Request Details & History | Department / Purchase Requests | Department/PurchaseRequestsController | Details | `Areas/Department/Views/PurchaseRequests/Details.cshtml` | Requesting Department (own department only) | UI scaffold; logic not implemented |
| Amal | Sourcing Officer Dashboard | Sourcing Officer / Tender Management | SourcingOfficer/DashboardController | Index | `Areas/SourcingOfficer/Views/Dashboard/Index.cshtml` | Sourcing Officer | Dashboard layout from ZIP; data not connected |
| Amal | Approved Purchase Requests | Sourcing Officer / Tender Management | SourcingOfficer/PurchaseRequestsController | Index | `Areas/SourcingOfficer/Views/PurchaseRequests/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Doulat | Purchase Request Review / Approval | Department / Purchase Requests (Sourcing Officer review step) | SourcingOfficer/PurchaseRequestsController | Review | `Areas/SourcingOfficer/Views/PurchaseRequests/Review.cshtml` | Sourcing Officer (business user). Doulat is the developer; the requesting department cannot use this page. | UI scaffold; logic not implemented |
| Amal | Manage Tenders | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | Index | `Areas/SourcingOfficer/Views/Tenders/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | Create Tender | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | Create | `Areas/SourcingOfficer/Views/Tenders/Create.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | Supplier Selection | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | SelectSuppliers | `Areas/SourcingOfficer/Views/Tenders/SelectSuppliers.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | Tender Timeline & Closing | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | Timeline | `Areas/SourcingOfficer/Views/Tenders/Timeline.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | — (no card) | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | Rounds | `Areas/SourcingOfficer/Views/Tenders/Rounds.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | Supplier Invitations | Sourcing Officer / Tender Management | SourcingOfficer/InvitationsController | Index | `Areas/SourcingOfficer/Views/Invitations/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Received Bids | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | Index | `Areas/SourcingOfficer/Views/Bids/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Bid Details & Documents | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | Details | `Areas/SourcingOfficer/Views/Bids/Details.cshtml` | Sourcing Officer (assigned committee members may later get a read-only view) | UI scaffold; logic not implemented |
| Saja | Bid Compliance Check | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | Compliance | `Areas/SourcingOfficer/Views/Bids/Compliance.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Bid Comparison | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | Compare | `Areas/SourcingOfficer/Views/Bids/Compare.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Forward Bids to Evaluation Committee | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | ForwardToCommittee | `Areas/SourcingOfficer/Views/Bids/ForwardToCommittee.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Bid & Clarification History | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | History | `Areas/SourcingOfficer/Views/Bids/History.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Supplier Clarification Requests | Sourcing Officer / Bid Management | SourcingOfficer/ClarificationsController | Index | `Areas/SourcingOfficer/Views/Clarifications/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Sourcing Officer / Reports | SourcingOfficer/ReportsController | Index | `Areas/SourcingOfficer/Views/Reports/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/DashboardController | Index | `Areas/Supplier/Views/Dashboard/Index.cshtml` | Supplier (own company only, Admin-approved) | Dashboard layout from ZIP; data not connected |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/ProfileController | Index | `Areas/Supplier/Views/Profile/Index.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/InvitationsController | Index | `Areas/Supplier/Views/Invitations/Index.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/InvitationsController | Details | `Areas/Supplier/Views/Invitations/Details.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/InvitationsController | Respond | `Areas/Supplier/Views/Invitations/Respond.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/OffersController | Index | `Areas/Supplier/Views/Offers/Index.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/OffersController | Create | `Areas/Supplier/Views/Offers/Create.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/OffersController | Edit | `Areas/Supplier/Views/Offers/Edit.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/OffersController | Details | `Areas/Supplier/Views/Offers/Details.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/OffersController | History | `Areas/Supplier/Views/Offers/History.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/DocumentsController | Index | `Areas/Supplier/Views/Documents/Index.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Supplier Portal | Supplier/ClarificationsController | Index | `Areas/Supplier/Views/Clarifications/Index.cshtml` | Supplier (own company only, Admin-approved) | UI scaffold; logic not implemented |
| Tariq | Committee Dashboard | Technical & Financial Evaluation Committee | EvaluationCommittee/DashboardController | Index | `Areas/EvaluationCommittee/Views/Dashboard/Index.cshtml` | Evaluation Committee member (assigned to the tender) | Dashboard layout from ZIP; data not connected |
| UNASSIGNED — Team assignment required | — (no card) | Technical & Financial Evaluation Committee | EvaluationCommittee/EvaluationsController | Index | `Areas/EvaluationCommittee/Views/Evaluations/Index.cshtml` | Evaluation Committee member (assigned to the tender) | UI scaffold; logic not implemented |
| Amal | Evaluation Criteria & Weights | Sourcing Officer / Tender Management (criteria screen used by the Evaluation Committee) | EvaluationCommittee/CriteriaController | Index | `Areas/EvaluationCommittee/Views/Criteria/Index.cshtml` | Evaluation Committee (edit). The Sourcing Officer may later get a read-only view. | UI scaffold; logic not implemented |
| Tariq | Technical Evaluation | Technical & Financial Evaluation Committee | EvaluationCommittee/EvaluationsController | Technical | `Areas/EvaluationCommittee/Views/Evaluations/Technical.cshtml` | Evaluation Committee member (assigned to the tender) | UI scaffold; logic not implemented |
| Tariq | Financial Evaluation | Technical & Financial Evaluation Committee | EvaluationCommittee/EvaluationsController | Financial | `Areas/EvaluationCommittee/Views/Evaluations/Financial.cshtml` | Evaluation Committee member (assigned to the tender) | UI scaffold; logic not implemented |
| Tariq | Committee Scoring & Consolidation | Technical & Financial Evaluation Committee | EvaluationCommittee/EvaluationsController | Scores | `Areas/EvaluationCommittee/Views/Evaluations/Scores.cshtml` | Evaluation Committee member (assigned to the tender) | UI scaffold; logic not implemented |
| Tariq | Ollama AI Bid Analysis | Technical & Financial Evaluation Committee | EvaluationCommittee/EvaluationsController | AiAnalysis | `Areas/EvaluationCommittee/Views/Evaluations/AiAnalysis.cshtml` | Evaluation Committee member (assigned to the tender) | UI scaffold; logic not implemented |
| Tariq | Evaluation History | Technical & Financial Evaluation Committee | EvaluationCommittee/EvaluationsController | History | `Areas/EvaluationCommittee/Views/Evaluations/History.cshtml` | Evaluation Committee member (assigned to the tender) | UI scaffold; logic not implemented |
| Tariq | Unified Evaluation Report | Technical & Financial Evaluation Committee | EvaluationCommittee/ReportsController | Unified | `Areas/EvaluationCommittee/Views/Reports/Unified.cshtml` | Evaluation Committee member (assigned to the tender) | UI scaffold; logic not implemented |
| Haya | Sourcing Manager Dashboard | Sourcing Manager Review | SourcingManager/DashboardController | Index | `Areas/SourcingManager/Views/Dashboard/Index.cshtml` | Sourcing Manager | Dashboard layout from ZIP; data not connected |
| Haya | Evaluation Reports List | Sourcing Manager Review | SourcingManager/ReportsController | Index | `Areas/SourcingManager/Views/Reports/Index.cshtml` | Sourcing Manager | UI scaffold; logic not implemented |
| Haya | Review Unified Evaluation Report | Sourcing Manager Review | SourcingManager/ReportsController | Review | `Areas/SourcingManager/Views/Reports/Review.cshtml` | Sourcing Manager | UI scaffold; logic not implemented |
| Haya | Approve Evaluation Report | Sourcing Manager Review | SourcingManager/ReportsController | Approve | `Areas/SourcingManager/Views/Reports/Approve.cshtml` | Sourcing Manager | UI scaffold; logic not implemented |
| Haya | Reject Evaluation Report | Sourcing Manager Review | SourcingManager/ReportsController | Reject | `Areas/SourcingManager/Views/Reports/Reject.cshtml` | Sourcing Manager | UI scaffold; logic not implemented |
| Haya | Return Report for Review | Sourcing Manager Review | SourcingManager/ReportsController | Return | `Areas/SourcingManager/Views/Reports/Return.cshtml` | Sourcing Manager | UI scaffold; logic not implemented |
| Haya | Forward to Top Committee | Sourcing Manager Review | SourcingManager/ReportsController | Forward | `Areas/SourcingManager/Views/Reports/Forward.cshtml` | Sourcing Manager | UI scaffold; logic not implemented |
| Haya | Sourcing Manager Decision History | Sourcing Manager Review | SourcingManager/ReportsController | History | `Areas/SourcingManager/Views/Reports/History.cshtml` | Sourcing Manager | UI scaffold; logic not implemented |
| Doulat | Top Committee Dashboard | Top Committee | TopCommittee/DashboardController | Index | `Areas/TopCommittee/Views/Dashboard/Index.cshtml` | Top Committee member (assigned to the tender) | Dashboard layout from ZIP; data not connected |
| Doulat | Review Tender Evaluation | Top Committee | TopCommittee/DecisionsController | ReviewEvaluation | `Areas/TopCommittee/Views/Decisions/ReviewEvaluation.cshtml` | Top Committee member (assigned to the tender) | UI scaffold; logic not implemented |
| Doulat | Committee Decision | Top Committee | TopCommittee/DecisionsController | Decision | `Areas/TopCommittee/Views/Decisions/Decision.cshtml` | Top Committee member (assigned to the tender) | UI scaffold; logic not implemented |
| Doulat | Committee Voting & Decision History | Top Committee | TopCommittee/DecisionsController | History | `Areas/TopCommittee/Views/Decisions/History.cshtml` | Top Committee member (assigned to the tender) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Top Committee | TopCommittee/StatisticsController | Index | `Areas/TopCommittee/Views/Statistics/Index.cshtml` | Top Committee member | UI scaffold; logic not implemented |
| Ahmad | User Login & Role Redirect | Authentication | WorkspaceController | Index | `Views/Workspace/Index.cshtml` | Any signed-in user | UI scaffold; logic not implemented |
| Ahmad | Fingerprint Registration & Login | Authentication & Biometrics | BiometricsController | Fingerprint | `Views/Biometrics/Fingerprint.cshtml` | Any signed-in user (enrolls their own account) | UI scaffold; logic not implemented |
| Ahmad | Face Registration & Recognition Login | Authentication & Biometrics | BiometricsController | Face | `Views/Biometrics/Face.cshtml` | Any signed-in user (enrolls their own account) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Shared workflow / Voice Search | TasksController | Index | `Views/Tasks/Index.cshtml` | Any signed-in user | UI scaffold; logic not implemented |
| ALL TEAM | Clarifications, Notifications & Audit Trail | Clarifications, Notifications & Audit Trail | NotificationsController | Index | `Views/Notifications/Index.cshtml` | Any signed-in user (own notifications only) | UI scaffold; logic not implemented |
| ALL TEAM | Clarifications, Notifications & Audit Trail | Clarifications, Notifications & Audit Trail | ClarificationsController | Index | `Views/Clarifications/Index.cshtml` | Any signed-in user (own threads only) | UI scaffold; logic not implemented |
| ALL TEAM | Clarifications, Notifications & Audit Trail | Clarifications, Notifications & Audit Trail | ClarificationsController | Details | `Views/Clarifications/Details.cshtml` | The two participants of the thread | UI scaffold; logic not implemented |
| Tariq | Home / Landing Page | Public Site & Shared Layout | Home | Index | `Views/Home/Index.cshtml` | Anonymous | Integrated static page |
| Tariq | — (no card) | Public Site | Home | About | `Views/Home/About.cshtml` | Anonymous | Integrated static page |
| Tariq | — (no card) | Public Site | Home | HowItWorks | `Views/Home/HowItWorks.cshtml` | Anonymous | Integrated static page |
| Tariq | Team Page | Public Site | Home | Team | `Views/Home/Team.cshtml` | Anonymous | Integrated static page |
| Tariq | — (no card) | Public Site | Home | Contact | `Views/Home/Contact.cshtml` | Anonymous | Integrated static page |
| UNASSIGNED — Team assignment required | — (no card) | Public Site | Home | Privacy | `Views/Home/Privacy.cshtml` | Anonymous | Integrated static page |
| ALL TEAM | Final Bug Fixing & Demo Preparation | Shared | Home | Error | `Views/Shared/Error.cshtml` | Anonymous | Integrated static page |
| ALL TEAM | Final Bug Fixing & Demo Preparation | Shared | Home | HttpStatus | `Views/Home/HttpStatus.cshtml` | Anonymous | Integrated static page |
| Tariq | — (no card) | Public Site | Contact | Submit (POST) | `— (JSON 501)` | Anonymous (antiforgery token) | Integrated static page |
| Ahmad | User Registration | Authentication | Identity: Account/Register | — | `Areas/Identity/Pages/Account/Register.cshtml` | Anonymous | Existing Identity behavior; restyled |
| Ahmad | User Login & Role Redirect | Authentication & Biometrics | Identity: Account/Login | — | `Areas/Identity/Pages/Account/Login.cshtml` | Anonymous | Existing Identity behavior; restyled |
| Ahmad | Forgot / Reset Password | Authentication | Identity: Account/ForgotPassword | — | `Areas/Identity/Pages/Account/ForgotPassword.cshtml` | Anonymous | Existing Identity behavior; restyled |
| Ahmad | Forgot / Reset Password | Authentication | Identity: Account/ResetPassword | — | `Areas/Identity/Pages/Account/ResetPassword.cshtml` | Anonymous (reset code) | Existing Identity behavior; restyled |
| Ahmad | User Login & Role Redirect | Authentication | Identity: Account/AccessDenied | — | `Areas/Identity/Pages/Account/AccessDenied.cshtml` | Signed-in user | Existing Identity behavior; restyled |
| Ahmad | — (no card) | Authentication | Identity (default UI) | — | `Microsoft.AspNetCore.Identity.UI (not scaffolded)` | Varies | Existing Identity behavior; restyled |

## 3. Required pages without a developer

These are in the approved workflow or specification but no Trello card covers them. They are marked
`UNASSIGNED — Team assignment required` inside the files.

- **Officer Reports** — `Areas/SourcingOfficer/Views/Reports/Index.cshtml`
- **Supplier Dashboard** — `Areas/Supplier/Views/Dashboard/Index.cshtml`
- **Company Profile** — `Areas/Supplier/Views/Profile/Index.cshtml`
- **My Invitations** — `Areas/Supplier/Views/Invitations/Index.cshtml`
- **Invitation Details** — `Areas/Supplier/Views/Invitations/Details.cshtml`
- **Accept / Decline Invitation** — `Areas/Supplier/Views/Invitations/Respond.cshtml`
- **My Submitted Offers** — `Areas/Supplier/Views/Offers/Index.cshtml`
- **Create Supplier Offer** — `Areas/Supplier/Views/Offers/Create.cshtml`
- **Edit Draft Offer** — `Areas/Supplier/Views/Offers/Edit.cshtml`
- **Offer Details** — `Areas/Supplier/Views/Offers/Details.cshtml`
- **Supplier Offer History** — `Areas/Supplier/Views/Offers/History.cshtml`
- **Supplier Documents** — `Areas/Supplier/Views/Documents/Index.cshtml`
- **Supplier Clarifications** — `Areas/Supplier/Views/Clarifications/Index.cshtml`
- **Assigned Evaluations** — `Areas/EvaluationCommittee/Views/Evaluations/Index.cshtml`
- **Management Statistics** — `Areas/TopCommittee/Views/Statistics/Index.cshtml`
- **My Tasks** — `Views/Tasks/Index.cshtml`
- **Privacy Policy** — `Views/Home/Privacy.cshtml`

## 4. Pages built by one developer but used by another role

| Page | Developer | Business user | Note |
|---|---|---|---|
| Purchase Request Review / Approval | Doulat | Sourcing Officer | Lives in the Sourcing Officer area. The department cannot open it. |
| Evaluation Criteria & Weights | Amal | Evaluation Committee | Committee edits; the Officer may later get a read-only view. |
| Approved Purchase Requests | Amal | Sourcing Officer | Its rows link to Doulat's Review page (Submitted) and Amal's Create Tender (Accepted). |
