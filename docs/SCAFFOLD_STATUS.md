# Scaffold Status

What exists and how it was verified (last audit: 13 Nov 2026). Paths are relative to the project folder
`Orange-Tendering-System/`. Business rules: [`TenderingOrangeSystem.md`](../TenderingOrangeSystem.md). Full audit:
[FINAL_SCAFFOLD_AND_DATABASE_AUDIT.md](FINAL_SCAFFOLD_AND_DATABASE_AUDIT.md).

## Summary

| Item | Count | Notes |
|---|---|---|
| MVC areas | 7 (+ Identity) | Admin, Department, SourcingOfficer, Supplier, EvaluationCommittee, SourcingManager, TopCommittee |
| Controllers | 39 | 31 area controllers; shared `Workspace`, `Biometrics`, `Tasks`, `Notifications`, `Clarifications`, `Analytics`; public `Home`, `Contact` |
| GET actions | 100 + 1 POST | the only POST is `Contact/Submit`, which deliberately answers 501 Not Implemented |
| Page views | 100 MVC + 5 scaffolded Identity pages | every action has a view and every view has an action |
| Layouts / partials | 3 layouts, 13 partials | see [LAYOUT_INTEGRATION.md](LAYOUT_INTEGRATION.md) |
| Trello cards mapped | 60 / 60, all with their real URL | see [PAGE_OWNERSHIP.md](PAGE_OWNERSHIP.md) |
| Database migrations added by scaffolding | 0 | schema unchanged since `AddBidComplianceAndRequestSnapshot` |

## Status legend for every workspace page

| Check | State |
|---|---|
| Route exists | ✅ all 105 documented GET routes respond |
| View exists | ✅ |
| Layout integrated | ✅ `_WorkspaceLayout` / `_SupplierLayout` / public `_Layout` |
| Owner comment | ✅ all 120 views and partials; page comments include developer, Trello card + URL (or "None"), purpose, business role, TODOs, dependencies, status |
| Authorization scaffold | ✅ area controllers `[Authorize(Roles = SystemRoles.X)]`; shared pages `[Authorize]`; `Analytics` limited to Admin, Sourcing Officer, Sourcing Manager, Top Committee |
| Business logic | ❌ not implemented — no database access in controllers, every data-changing control disabled |

Dashboards reuse the supplied layout's dashboard bodies and show `—` / "Not connected" until data is bound.

## How it was verified

1. `dotnet build` — **0 warnings, 0 errors** (built into a separate output folder because a running copy of the
   app locked `bin/`).
2. Static code audit (script, not committed): every controller has an owner header and `[Authorize]` where required;
   no `DbContext`, `SaveChanges` or POST outside `ContactController`; no service classes; no enabled submit buttons;
   no `TRELLO URL: Not provided` left; no files changed under `Data/` or `Models/Entities/`.
3. Runtime check with a temporary test host (not committed) running the real app with a test-only sign-in:
   - **105** routes from ROUTE_MAP rendered with the correct layout;
   - **140** link and asset targets found on those pages returned 200 (navigation, breadcrumbs, related links,
     CSS, JS, images, `team.json`);
   - anonymous requests to protected pages were redirected to sign-in; a Supplier was refused (403) on Admin and
     Analytics pages; an unknown URL returned the friendly 404 page.
   - The only non-200 is `/Identity/Account/ResetPassword` without a code, which Identity rejects with 400 by design.
4. Headless Chrome screenshots at 1440 px (dark and light) and 390 px: no horizontal page scroll on workspace pages.
5. Database: EF reports **no pending model changes**; all 3 migrations applied; live SQL Server has 30 tables.

## Known limitations

- **Roles are not seeded**, so with a real account every workspace page shows *Access denied*. Correct until
  Ahmad's role/account provisioning exists.
- Non-scaffolded Identity pages (`/Identity/Account/Manage`, confirmations, lockout) keep the default Identity UI
  markup styled by `identity.css`; they were not visually reviewed in depth.
- No email sender is configured (registration confirmation and password reset emails are not sent).
- The contact form endpoint answers *501 Not Implemented* on purpose.
- Real-account sign-in was not tested end to end (no roles or users exist).
