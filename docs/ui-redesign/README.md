# Orange UI redesign — 13 November 2026

Branch: `codex/orange-ui-redesign-20261113` (nothing committed yet). Screenshots: `docs/ui-redesign/screenshots/` (genuine Playwright captures from the running app, dark and light).

## What changed
- **One shell for all roles.** The six per-area layout copies were removed; every internal role uses `Views/Shared/_WorkspaceLayout.cshtml` (sidebar + topbar). Supplier uses the new premium **horizontal navbar** (`Views/Shared/_SupplierLayout.cshtml`), matching the supplied supplier screenshot.
- **Real dashboards.** The six internal dashboards were empty scaffolds ("Not connected"). `Services/InternalDashboardService.cs` now computes role-scoped KPIs, trend %, ongoing tender, workflow stage, tasks, tender status donut and recent activity from SQL Server. They share `Views/Shared/_InternalDashboard.cshtml`. Supplier's participation chart is real (offers submitted per month).
- **Live notification bell** (all roles): SQL `Notifications` rows of the signed-in user only; unread badge, dropdown, mark read / mark all read, relevant links, toast. Real-time via SignalR (`Hubs/NotificationHub.cs`, `/hubs/notifications`); an EF `SaveChangesInterceptor` pushes every new `Notification` row so existing workflows needed no change. Polling fallback if the hub is unreachable. SignalR JS client 8.0.7 is vendored locally.
- **Global search + voice search** (`/Search/Query`, role-scoped) replace the disabled search box. **My Tasks** is now real.
- **Popup-first UX** (`wwwroot/js/orange-ui.js`): Create / Edit / Approve / Reject / Review / Upload… links open as centered modals, Details / Review as right-hand drawers; forms submit in place. Complex workflow pages (`/Workflow`, Identity, biometrics) stay full pages on purpose.
- **Table kit**: search, status filter, column sorting, pagination, status pills and mobile cards on every table outside the dashboards.
- Bare legacy views (Users, Purchase Requests, Supplier review, Workflow index) were rebuilt in the shared design.
- Sidebar no longer shows Weighted Evaluation / Weight Approvals to roles that would get a 400 (policy-aware).

## Verification (all on isolated `OrangeAuthCheck_UiE2E_*` SQL Server database, real app on https://localhost:5099)
- Release build of the web project: 0 warnings, 0 errors. Existing auth harness (`Tests/AuthSmoke`): 211 checks pass.
- Playwright suite `tests/ui_suite.py`: **76/76** — all seven roles sign in and render in dark and light with no JavaScript errors; KPIs equal SQL counts; bell badge/list/mark-read equal SQL; a Department user's popup-created request pushes a live notification to the Officer's bell over SignalR; drawer/modal/search/table checks; mobile (390px) has no horizontal overflow.
- Sweep `tests/ui_sweep.py`: every sidebar/navbar page of every role (72 pages) loads in dark and light with HTTP 200 and no console errors.

## Not covered
Signing, Zoom, Ollama and report-PDF flows were not re-exercised through the browser; they were not modified. Pages with large bespoke forms (offer create/edit, report review, workflow details) keep their full-page flow and receive the shared styling only.
