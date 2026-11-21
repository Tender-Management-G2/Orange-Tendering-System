# Orange AI — role / permission / operation matrix

This matrix lists the business operations that exist in the application, which ones the Orange AI agent may run, how each is
authorized, and how its result is verified. It is derived from the code (`Models/SystemRoles.cs`, area `[Authorize]` attributes,
`Services/*`, `Areas/*/Services/*`). Nothing here is assumed: an operation is listed as an agent tool only when the agent calls
the same application service the web page uses.

## Roles (Models/SystemRoles.cs)

| Role | Workspace (area) | Data scope used by the agent (`AssistantAccess`) |
|---|---|---|
| Admin | `/Admin` | Accounts, departments, suppliers, audit. No confidential tender rows. |
| Requesting Department | `/Department` | Own department's purchase requests and their tenders. |
| Sourcing Officer (single active) | `/SourcingOfficer` | All tenders, submitted purchase requests, suppliers. |
| Sourcing Manager | `/SourcingManager` | Tenders where seated as Sourcing Manager. |
| Evaluation Committee (Technical, Finance, Department Representative) | `/EvaluationCommittee` | Tenders where seated on the evaluation committee. |
| Top Committee (CEO, CFO, CTO, COO) | `/TopCommittee` | Tenders where seated on the Top Committee. |
| Supplier | `/Supplier` | Own invitations, own offers, own notifications. |

Committee permissions come from `SystemRoles.AccountRoles` (e.g. Technical ⇒ Technical + Evaluation Committee).

## Agent architecture

```
message (typed or transcribed voice)
  → AssistantIntentParser / AgentTools.Detect   (deterministic)   → else model router (one allow-listed action, validated)
  → AgentTools.Denial(tool, user)               (permission from the authenticated principal + SQL Server)
  → slot collection + AgentTools.Validate       (typed values; duplicates, roles, departments, dates checked against the DB)
  → summary with side effects                   (state "Confirming", pending ActionId, 30-minute expiry)
  → explicit confirmation                       ("confirm/أكّد" or the Confirm button carrying the same ActionId)
  → permission re-check → existing service      (AdminService, EmployeeProvisioning, DepartmentRequestService, TenderWorkflowService)
  → verification query → AuditLogs              (AiAgentStep "AiToolExecuted" per ActionId:step = idempotency key)
  → per-step report (partial outcomes reported as partial)
```

The model never receives SQL, DbContext, routes to call, or permission decisions. URLs shown or navigated to are built by the
server from fixed templates or from `ExecutiveNavigationService.Catalog` (registered GET actions the user is authorized for).

## Operations

Legend — **Agent**: ✅ executed by the agent after confirmation · 🔎 read-only, immediate · 🛡 prepared only, executed after
passkey/password confirmation (existing Executive Agent) · ⛔ not exposed (high impact or needs the full page).

| Module | Operation | Existing service | Permission | Confirmation | Reversible | Agent | Verification |
|---|---|---|---|---|---|---|---|
| Departments | Create department | `AdminService.CreateDepartmentAsync` | Admin | Yes | Yes (deactivate) | ✅ `create_department` | Department row by name → ID |
| Departments | Rename department | `AdminService.UpdateDepartmentAsync` (current employees passed through) | Admin | Yes | Yes | ✅ `rename_department` | New name + unchanged employee count |
| Departments | List departments | read | Admin | No | — | 🔎 `list_departments` | — |
| Procurement categories | Create active category | `ProcurementCategoryService.SaveAsync`, shared with Admin Categories | Admin | Yes | Deactivate from Admin Categories | ✅ `create_category` | Read back the new active category and record a category audit entry |
| Departments | Change members / deactivate | `AdminService.UpdateDepartmentAsync` | Admin | — | — | ⛔ (page) | — |
| Users | Create employee | `EmployeeProvisioning.CreateAsync` (no password; activation link) | Admin | Yes | No (deactivate only) | ✅ `create_user` | User by email + role membership |
| Users | Create with temporary password | `AdminService.CreateUserAsync` | Admin | — | — | ⛔ (AI must not handle passwords) | — |
| Users | List/find/details | `AdminService.GetUsersAsync/GetUserDetailsAsync` + scoped employee lookup | Admin | No | — | 🔎 `list_users`, `find_user`, `get_user_details` | Read from current account/role/department state; lookup audit hash |
| Users | Change name/phone/department, deactivate/reactivate | `AdminService.UpdateUserProfileAsync` (no role parameter) | Admin | Explicit plan confirmation; Admin accounts protected | Yes (except downstream workflow consequences) | ✅ `update_user_name`, `update_user_phone`, `change_user_department`, `deactivate_user`, `reactivate_user` | Re-read account and confirm requested fields + role set unchanged |
| Users | Assign/remove role | `AdminService.AssignRoleAsync/RemoveRoleAsync/UpdateUserAsync` | Admin | Existing protected page workflow; AI lacks strong re-auth | — | 🛡 `prepare_user_role_change` only (computes a validated before/after summary and opens `/Admin/Roles`; performs no write) | Admin page is authoritative |
| Users | Grant Admin role | — | Admin | — | — | ⛔ (no AI privilege escalation) | — |
| Suppliers | Review registration / documents | `AdminService.ReviewSupplierAsync/ReviewSupplierDocumentAsync` | Admin | — | — | ⛔ (page) | — |
| Suppliers | List / performance / export | read (`AssistantDataTools`) | Admin, Officer (+ Manager/Top for performance) | No | — | 🔎 | — |
| Purchase requests | Create draft | `DepartmentRequestService.Save(submit:false)` | Requesting Department, own active department | Yes | Yes (edit/discard draft) | ✅ `create_purchase_request` | PR row, Draft, own department → PR number |
| Purchase requests | Submit to Officer | `DepartmentRequestService.Save(submit:true)` | Requesting Department | — | — | ⛔ (user submits from the page) | — |
| Purchase requests | Review (accept/return/reject) | `PurchaseRequestService.Decide` | Sourcing Officer | — | — | ⛔ | — |
| Purchase requests | List / delayed / export | read | Department, Officer | No | — | 🔎 | — |
| Tenders | Create draft | `TenderWorkflowService.Create` | Single active Sourcing Officer | Yes | Draft | ✅ (tender draft conversation) | Tender row, Draft, committee seated |
| Tenders | Open latest / by ID / search / drafts / awaiting approval / attachments | read | Scoped per role | No | — | 🔎 | — |
| Tenders | Request weights, forward report, notify suppliers, approve weights | `TenderWorkflowService.*` | Officer | Passkey/password | No | 🛡 (Executive Agent) | Action state Succeeded |
| Evaluation | Propose weights | `TenderWorkflowService.Propose` | Seated evaluator | Yes | No (immutable) | ✅ (weights conversation) | WeightProposal row |
| Evaluation | Score offers | workflow page | Seated evaluator | — | No | ⛔ (opened for the user: "evaluate") | — |
| Reports | Sign evaluation report / Manager decision / Top vote | `TenderWorkflowService.FinalizeReport/ManagerDecision/TopVote` | Seated member | Passkey | No | 🛡 (Executive Agent; "which report needs my signature" opens it) | — |
| Tenders | Publish/open bidding, award, cancel, delete | `TenderWorkflowService.OpenBidding/...` | Officer | — | No | ⛔ (high impact, disabled) | — |
| Files | Excel / PDF / Word / PowerPoint reports | `AssistantExcel/Pdf/Word/Slides` | Scoped data | No (read) | — | 🔎 | Saved file, owner-only download |
| Files | Uploaded document analysis | `AiChatFileStore` + Ollama | Any signed-in user, own files | No | — | 🔎 | — |
| Navigation | Go to an authorized page | `ExecutiveNavigationService.Catalog/Resolve` | Authorized GET pages | No | — | 🔎 | Catalog membership |

## Persistent knowledge (RAG)

- `AiKnowledgeService` supports private personal knowledge and shared knowledge that remains `PendingApproval` until an active Admin approves it.
- Uploads are parsed by the existing bounded document extractor, previewed, summarized, and explicitly saved. Supported source types are the existing PDF, XLSX, DOCX, CSV, and TXT formats; OCR/scanned-document and image ingestion are not claimed.
- Embeddings use configured Ollama `/api/embed` (default `bge-m3`); vectors and metadata are persisted in SQL Server, and cosine similarity plus keyword overlap are ranked in application code. No SQL Server vector feature is assumed.
- Search filters to the signed-in user's personal records and approved shared/system records before ranking. Retrieved text is marked untrusted and cannot grant tool permissions; answer citations identify document/version/chunk.
- Admin knowledge screen actions include review, replacement, deletion, and re-indexing. Application-generated knowledge is versioned and starts pending approval.
- Database-backed retrieval/authorization persistence tests still require a separately configured isolated SQL Server test database and have not been verified here.

## Not implemented in this stage
- Updating tender fields after creation (requirements are frozen once weights are requested by the existing workflow).
- Editing existing purchase request drafts and submitting them through the agent.
- Direct role assignment/removal through chat; strong re-authentication is not integrated into the chat tool execution flow, so role changes are prepared for the protected existing page only.
- Supplier record edits and every ⛔ operation above.
