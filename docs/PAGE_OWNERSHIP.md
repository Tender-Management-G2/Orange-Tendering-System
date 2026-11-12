# Page Ownership

Generated from the scaffold manifest. **Developer owner** = who builds it. **Business role** = who may use it.
Trello URLs come from `TenderingOrangeSystem.md` §18 (board: https://trello.com/b/407Mnreu/tender-management-system). Product rules live in that file; this document only maps work to files.
File paths are relative to the project folder `Orange-Tendering-System/`.

## 1. Trello card coverage (60 cards)

| # | Developer | Trello card | Trello URL | Implemented by (view or source) |
|---|---|---|---|---|
| 1 | Doulat | Department Dashboard | https://trello.com/c/033sNEkN | `Areas/Department/Views/Dashboard/Index.cshtml` |
| 2 | Doulat | Create Purchase Request | https://trello.com/c/vCxUl1Bb | `Areas/Department/Views/PurchaseRequests/Create.cshtml` |
| 3 | Doulat | My Purchase Requests | https://trello.com/c/SjSwbYUx | `Areas/Department/Views/PurchaseRequests/Index.cshtml` |
| 4 | Doulat | Purchase Request Details & History | https://trello.com/c/IR3EpX2r | `Areas/Department/Views/PurchaseRequests/Details.cshtml` |
| 5 | Doulat | Purchase Request Review / Approval | https://trello.com/c/cOYVH9co | `Areas/SourcingOfficer/Views/PurchaseRequests/Review.cshtml` |
| 6 | Doulat | Top Committee Dashboard | https://trello.com/c/8Z6bnMsb | `Areas/TopCommittee/Views/Dashboard/Index.cshtml` |
| 7 | Doulat | Review Tender Evaluation | https://trello.com/c/EDdybTCg | `Areas/TopCommittee/Views/Decisions/ReviewEvaluation.cshtml` |
| 8 | Doulat | Committee Decision | https://trello.com/c/RtPahsxa | `Areas/TopCommittee/Views/Decisions/Decision.cshtml` |
| 9 | Doulat | Committee Voting & Decision History | https://trello.com/c/3NtWOXwd | `Areas/TopCommittee/Views/Decisions/History.cshtml` |
| 10 | Haya | Sourcing Manager Dashboard | https://trello.com/c/ZkMN8s14 | `Areas/SourcingManager/Views/Dashboard/Index.cshtml` |
| 11 | Haya | Evaluation Reports List | https://trello.com/c/CWgcYFbk | `Areas/SourcingManager/Views/Reports/Index.cshtml` |
| 12 | Haya | Review Unified Evaluation Report | https://trello.com/c/IKQ7sZwo | `Areas/SourcingManager/Views/Reports/Review.cshtml` |
| 13 | Haya | Approve Evaluation Report | https://trello.com/c/tpvo9nZx | `Areas/SourcingManager/Views/Reports/Approve.cshtml` |
| 14 | Haya | Reject Evaluation Report | https://trello.com/c/WE4m7Kej | `Areas/SourcingManager/Views/Reports/Reject.cshtml` |
| 15 | Haya | Return Report for Review | https://trello.com/c/r1oVUbDr | `Areas/SourcingManager/Views/Reports/Return.cshtml` |
| 16 | Haya | Forward to Top Committee | https://trello.com/c/953NoZV0 | `Areas/SourcingManager/Views/Reports/Forward.cshtml` |
| 17 | Haya | Sourcing Manager Decision History | https://trello.com/c/CSb7lOgK | `Areas/SourcingManager/Views/Reports/History.cshtml` |
| 18 | Ahmad | User Registration | https://trello.com/c/vYTVxn8o | `Areas/Identity/Pages/Account/Register.cshtml` |
| 19 | Ahmad | User Login & Role Redirect | https://trello.com/c/WoTewqHd | `Views/Workspace/Index.cshtml`<br>`Areas/Identity/Pages/Account/Login.cshtml`<br>`Areas/Identity/Pages/Account/AccessDenied.cshtml` |
| 20 | Ahmad | Forgot / Reset Password | https://trello.com/c/uLN8NsSX | `Areas/Identity/Pages/Account/ForgotPassword.cshtml`<br>`Areas/Identity/Pages/Account/ResetPassword.cshtml` |
| 21 | Ahmad | Fingerprint Registration & Login | https://trello.com/c/mQphpvOG | `Views/Biometrics/Fingerprint.cshtml` |
| 22 | Ahmad | Face Registration & Recognition Login | https://trello.com/c/tWcVmtdC | `Views/Biometrics/Face.cshtml` |
| 23 | Ahmad | Database Design & ERD | https://trello.com/c/MfKzPhJ2 | Models/Entities/*.cs, Data/ApplicationDbContext.cs |
| 24 | Ahmad | Database Implementation & Seed Data | https://trello.com/c/n8KcgpBJ | Data/Migrations/*, Models/SystemRoles.cs (role seeding TODO) |
| 25 | Ahmad | GitHub Repository & Team Workflow | https://trello.com/c/2HhEKtcD | Repository, .gitignore, docs/TEAM_HANDOFF.md |
| 26 | Amal | Sourcing Officer Dashboard | https://trello.com/c/VeRtD4Tn | `Areas/SourcingOfficer/Views/Dashboard/Index.cshtml` |
| 27 | Amal | Approved Purchase Requests | https://trello.com/c/bsQzN4ih | `Areas/SourcingOfficer/Views/PurchaseRequests/Index.cshtml` |
| 28 | Amal | Create Tender | https://trello.com/c/907F6ufP | `Areas/SourcingOfficer/Views/Tenders/Create.cshtml` |
| 29 | Amal | Manage Tenders | https://trello.com/c/e2e0QlHT | `Areas/SourcingOfficer/Views/Tenders/Index.cshtml`<br>`Areas/SourcingOfficer/Views/Tenders/Details.cshtml`<br>`Areas/SourcingOfficer/Views/Tenders/Edit.cshtml` |
| 30 | Amal | Evaluation Criteria & Weights | https://trello.com/c/ruVwkBHz | `Areas/EvaluationCommittee/Views/Criteria/Index.cshtml` |
| 31 | Amal | Supplier Selection | https://trello.com/c/CDiXqUwu | `Areas/SourcingOfficer/Views/Tenders/SelectSuppliers.cshtml` |
| 32 | Amal | Supplier Invitations | https://trello.com/c/Qh7GKR8x | `Areas/SourcingOfficer/Views/Invitations/Index.cshtml` |
| 33 | Amal | Tender Timeline & Closing | https://trello.com/c/zNRW9odq | `Areas/SourcingOfficer/Views/Tenders/Timeline.cshtml` |
| 34 | Saja | Received Bids | https://trello.com/c/06s4zwV2 | `Areas/SourcingOfficer/Views/Bids/Index.cshtml` |
| 35 | Saja | Bid Details & Documents | https://trello.com/c/yTyri6lN | `Areas/SourcingOfficer/Views/Bids/Details.cshtml` |
| 36 | Saja | Bid Compliance Check | https://trello.com/c/oDUNPe8T | `Areas/SourcingOfficer/Views/Bids/Compliance.cshtml` |
| 37 | Saja | Supplier Clarification Requests | https://trello.com/c/oKUyoBCd | `Areas/SourcingOfficer/Views/Clarifications/Index.cshtml` |
| 38 | Saja | Bid Comparison | https://trello.com/c/nqTrGMXs | `Areas/SourcingOfficer/Views/Bids/Compare.cshtml` |
| 39 | Saja | Forward Bids to Technical & Financial Committee | https://trello.com/c/oYtRrID9 | `Areas/SourcingOfficer/Views/Bids/ForwardToCommittee.cshtml` |
| 40 | Saja | Bid & Clarification History | https://trello.com/c/GqLKg6G2 | `Areas/SourcingOfficer/Views/Bids/History.cshtml` |
| 41 | Tariq | Home / Landing Page | https://trello.com/c/QMrx8dA6 | `Views/Home/Index.cshtml` |
| 42 | Tariq | Team Page | https://trello.com/c/P1gL4sPq | `Views/Home/Team.cshtml` |
| 43 | Tariq | Shared Layout & Responsive Design | https://trello.com/c/CiGKUw2k | Views/Shared/_Layout, _WorkspaceLayout, _SupplierLayout, _Sidebar, _PageHeader, …; wwwroot/css, wwwroot/js |
| 44 | Tariq | Committee Dashboard | https://trello.com/c/Z1VGmfZ7 | `Areas/EvaluationCommittee/Views/Dashboard/Index.cshtml` |
| 45 | Tariq | Technical Evaluation | https://trello.com/c/ocle0nNz | `Areas/EvaluationCommittee/Views/Evaluations/Technical.cshtml` |
| 46 | Tariq | Financial Evaluation | https://trello.com/c/oqDGzKzR | `Areas/EvaluationCommittee/Views/Evaluations/Financial.cshtml` |
| 47 | Tariq | Committee Scoring & Consolidation | https://trello.com/c/zzYbN4wd | `Areas/EvaluationCommittee/Views/Evaluations/Scores.cshtml` |
| 48 | Tariq | Ollama AI Bid Analysis | https://trello.com/c/oWr6Ezhy | `Areas/EvaluationCommittee/Views/Evaluations/AiAnalysis.cshtml` |
| 49 | Tariq | Unified Evaluation Report | https://trello.com/c/ssHM0e0R | `Areas/EvaluationCommittee/Views/Reports/Unified.cshtml` |
| 50 | Tariq | Evaluation History | https://trello.com/c/gmOCr3ie | `Areas/EvaluationCommittee/Views/Evaluations/History.cshtml` |
| 51 | Rjoub | Admin Dashboard | https://trello.com/c/55BjFqvf | `Areas/Admin/Views/Dashboard/Index.cshtml` |
| 52 | Rjoub | User Management | https://trello.com/c/UYxdWeUv | `Areas/Admin/Views/Users/Index.cshtml`<br>`Areas/Admin/Views/Users/Create.cshtml`<br>`Areas/Admin/Views/Users/Edit.cshtml`<br>`Areas/Admin/Views/Users/Details.cshtml` |
| 53 | Rjoub | Role & Permission Management | https://trello.com/c/eyB6myRo | `Areas/Admin/Views/Roles/Index.cshtml` |
| 54 | Rjoub | Department Management | https://trello.com/c/KHRsEmzE | `Areas/Admin/Views/Departments/Index.cshtml`<br>`Areas/Admin/Views/Departments/Create.cshtml`<br>`Areas/Admin/Views/Departments/Edit.cshtml` |
| 55 | Rjoub | Supplier Administration | https://trello.com/c/zvMwn4LD | `Areas/Admin/Views/Suppliers/Index.cshtml`<br>`Areas/Admin/Views/Suppliers/Review.cshtml`<br>`Areas/Admin/Views/Suppliers/Details.cshtml` |
| 56 | Rjoub | System Settings | https://trello.com/c/26iJGjhv | `Areas/Admin/Views/Settings/Index.cshtml` |
| 57 | Rjoub | Audit Logs | https://trello.com/c/eL9BCtiB | `Areas/Admin/Views/AuditLogs/Index.cshtml` |
| 58 | ALL TEAM | End-to-End Workflow Testing | https://trello.com/c/X9TNSrke | docs/ROUTE_MAP.md (end-to-end checklist), docs/TEAM_HANDOFF.md (testing) |
| 59 | ALL TEAM | Clarifications, Notifications & Audit Trail | https://trello.com/c/N1OFBdNM | `Views/Notifications/Index.cshtml`<br>`Views/Clarifications/Index.cshtml`<br>`Views/Clarifications/Details.cshtml`<br>AuditLogs table; Admin/AuditLogs page; audit TODOs in every page |
| 60 | ALL TEAM | Final Bug Fixing & Demo Preparation | https://trello.com/c/Z2QhEmfP | `Views/Shared/Error.cshtml`<br>`Views/Home/HttpStatus.cshtml`<br>docs/SCAFFOLD_STATUS.md |

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
| Doulat | — (no card) | Department / Purchase Requests | Department/PurchaseRequestsController | Edit | `Areas/Department/Views/PurchaseRequests/Edit.cshtml` | Requesting Department (original owner / own department; only Draft or Returned requests) | UI scaffold; logic not implemented |
| Amal | Sourcing Officer Dashboard | Sourcing Officer / Tender Management | SourcingOfficer/DashboardController | Index | `Areas/SourcingOfficer/Views/Dashboard/Index.cshtml` | Sourcing Officer | Dashboard layout from ZIP; data not connected |
| Amal | Approved Purchase Requests | Sourcing Officer / Tender Management | SourcingOfficer/PurchaseRequestsController | Index | `Areas/SourcingOfficer/Views/PurchaseRequests/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Doulat | Purchase Request Review / Approval | Department / Purchase Requests (Sourcing Officer review step) | SourcingOfficer/PurchaseRequestsController | Review | `Areas/SourcingOfficer/Views/PurchaseRequests/Review.cshtml` | Sourcing Officer (business user). Doulat is the developer; the requesting department cannot use this page. | UI scaffold; logic not implemented |
| Amal | Manage Tenders | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | Index | `Areas/SourcingOfficer/Views/Tenders/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | Manage Tenders | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | Details | `Areas/SourcingOfficer/Views/Tenders/Details.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | Manage Tenders | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | Edit | `Areas/SourcingOfficer/Views/Tenders/Edit.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Committees (tender-specific membership) | SourcingOfficer/TendersController | Committees | `Areas/SourcingOfficer/Views/Tenders/Committees.cshtml` | Business role NOT yet decided (Sourcing Officer or Admin) — placed in the Sourcing Officer area until the team decides. | UI scaffold; logic not implemented |
| Amal | Create Tender | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | Create | `Areas/SourcingOfficer/Views/Tenders/Create.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | Supplier Selection | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | SelectSuppliers | `Areas/SourcingOfficer/Views/Tenders/SelectSuppliers.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | Tender Timeline & Closing | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | Timeline | `Areas/SourcingOfficer/Views/Tenders/Timeline.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | — (no card) | Sourcing Officer / Tender Management | SourcingOfficer/TendersController | Rounds | `Areas/SourcingOfficer/Views/Tenders/Rounds.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Amal | Supplier Invitations | Sourcing Officer / Tender Management | SourcingOfficer/InvitationsController | Index | `Areas/SourcingOfficer/Views/Invitations/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Received Bids | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | Index | `Areas/SourcingOfficer/Views/Bids/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Bid Details & Documents | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | Details | `Areas/SourcingOfficer/Views/Bids/Details.cshtml` | Sourcing Officer (assigned committee members may later get a read-only view) | UI scaffold; logic not implemented |
| Saja | Bid Compliance Check | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | Compliance | `Areas/SourcingOfficer/Views/Bids/Compliance.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Bid Comparison | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | Compare | `Areas/SourcingOfficer/Views/Bids/Compare.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Forward Bids to Technical & Financial Committee | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | ForwardToCommittee | `Areas/SourcingOfficer/Views/Bids/ForwardToCommittee.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Bid & Clarification History | Sourcing Officer / Bid Management | SourcingOfficer/BidsController | History | `Areas/SourcingOfficer/Views/Bids/History.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| Saja | Supplier Clarification Requests | Sourcing Officer / Bid Management | SourcingOfficer/ClarificationsController | Index | `Areas/SourcingOfficer/Views/Clarifications/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Sourcing Officer / Reports | SourcingOfficer/ReportsController | Index | `Areas/SourcingOfficer/Views/Reports/Index.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Sourcing Officer / Reports | SourcingOfficer/ReportsController | Comparison | `Areas/SourcingOfficer/Views/Reports/Comparison.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Sourcing Officer / Reports | SourcingOfficer/ReportsController | Details | `Areas/SourcingOfficer/Views/Reports/Details.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Sourcing Officer / Reports | SourcingOfficer/ReportsController | EvaluationResult | `Areas/SourcingOfficer/Views/Reports/EvaluationResult.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Sourcing Officer / Reports | SourcingOfficer/ReportsController | SubmitToManager | `Areas/SourcingOfficer/Views/Reports/SubmitToManager.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Sourcing Officer / Reports | SourcingOfficer/ReportsController | Returned | `Areas/SourcingOfficer/Views/Reports/Returned.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Sourcing Officer / Reports | SourcingOfficer/ReportsController | History | `Areas/SourcingOfficer/Views/Reports/History.cshtml` | Sourcing Officer | UI scaffold; logic not implemented |
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
| UNASSIGNED — Team assignment required | — (no card) | Reports, Analytics & Exports | AnalyticsController | Index | `Views/Analytics/Index.cshtml` | Admin, Sourcing Officer, Sourcing Manager, Top Committee (exact audience per report to be confirmed; results must be role- and ownership-filtered) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Reports, Analytics & Exports | AnalyticsController | PurchaseRequests | `Views/Analytics/PurchaseRequests.cshtml` | Admin, Sourcing Officer, Sourcing Manager, Top Committee (exact audience per report to be confirmed; results must be role- and ownership-filtered) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Reports, Analytics & Exports | AnalyticsController | Tenders | `Views/Analytics/Tenders.cshtml` | Admin, Sourcing Officer, Sourcing Manager, Top Committee (exact audience per report to be confirmed; results must be role- and ownership-filtered) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Reports, Analytics & Exports | AnalyticsController | Suppliers | `Views/Analytics/Suppliers.cshtml` | Admin, Sourcing Officer, Sourcing Manager, Top Committee (exact audience per report to be confirmed; results must be role- and ownership-filtered) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Reports, Analytics & Exports | AnalyticsController | Evaluations | `Views/Analytics/Evaluations.cshtml` | Admin, Sourcing Officer, Sourcing Manager, Top Committee (exact audience per report to be confirmed; results must be role- and ownership-filtered) | UI scaffold; logic not implemented |
| UNASSIGNED — Team assignment required | — (no card) | Reports, Analytics & Exports | AnalyticsController | Governance | `Views/Analytics/Governance.cshtml` | Admin, Sourcing Officer, Sourcing Manager, Top Committee (exact audience per report to be confirmed; results must be role- and ownership-filtered) | UI scaffold; logic not implemented |
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

- **Committee Assignment** — `Areas/SourcingOfficer/Views/Tenders/Committees.cshtml`
- **Officer Reports** — `Areas/SourcingOfficer/Views/Reports/Index.cshtml`
- **Officer Comparison Report** — `Areas/SourcingOfficer/Views/Reports/Comparison.cshtml`
- **Report Details** — `Areas/SourcingOfficer/Views/Reports/Details.cshtml`
- **Committee Result Received** — `Areas/SourcingOfficer/Views/Reports/EvaluationResult.cshtml`
- **Submit Full Report to Sourcing Manager** — `Areas/SourcingOfficer/Views/Reports/SubmitToManager.cshtml`
- **Returned Report Review** — `Areas/SourcingOfficer/Views/Reports/Returned.cshtml`
- **Report Versions** — `Areas/SourcingOfficer/Views/Reports/History.cshtml`
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
- **Reports & Analytics** — `Views/Analytics/Index.cshtml`
- **Purchase Request Reports** — `Views/Analytics/PurchaseRequests.cshtml`
- **Tender Reports** — `Views/Analytics/Tenders.cshtml`
- **Supplier Reports** — `Views/Analytics/Suppliers.cshtml`
- **Bids & Evaluation Reports** — `Views/Analytics/Evaluations.cshtml`
- **Approvals & Governance Reports** — `Views/Analytics/Governance.cshtml`
- **Privacy Policy** — `Views/Home/Privacy.cshtml`

## 4. Pages built by one developer but used by another role

| Page | Developer | Business user | Note |
|---|---|---|---|
| Purchase Request Review / Approval | Doulat | Sourcing Officer | Lives in the Sourcing Officer area. The department cannot open it. |
| Evaluation Criteria & Weights | Amal | Evaluation Committee | Committee edits; the Officer may later get a read-only view. |
| Approved Purchase Requests | Amal | Sourcing Officer | Its rows link to Doulat's Review page (Submitted) and Amal's Create Tender (Accepted). |
| Committee Assignment | UNASSIGNED | Not decided (Sourcing Officer or Admin) | Placed in the Sourcing Officer area until the team decides. |
