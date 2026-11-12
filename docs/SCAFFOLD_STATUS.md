# Scaffold Status

What exists after the MVC scaffolding task and how it was verified (13 Nov 2026). Paths are relative to the
project folder `Orange-Tendering-System/`.

## Summary

| Item | Count | Notes |
|---|---|---|
| MVC areas | 7 | Admin, Department, SourcingOfficer, Supplier, EvaluationCommittee, SourcingManager, TopCommittee (+ Identity) |
| New controllers | 37 | 31 area controllers, 5 shared (`Workspace`, `Biometrics`, `Tasks`, `Notifications`, `Clarifications`), `Contact` |
| Changed controllers | 1 | `HomeController` (public routes from the layout ZIP + status page) |
| Page views | 76 generated + 5 Identity + 6 public | every page has a Razor ownership comment |
| Layouts | 3 | `_Layout` (public + Identity), `_WorkspaceLayout` (6 internal roles), `_SupplierLayout` |
| Shared partials | 13 | listed in [LAYOUT_INTEGRATION.md](LAYOUT_INTEGRATION.md) |
| Trello cards mapped | 60 / 60 | see [PAGE_OWNERSHIP.md](PAGE_OWNERSHIP.md) |
| Database migrations added | 0 | schema unchanged |

## Status legend for every workspace page

| Check | State |
|---|---|
| Route exists | ✅ all 89 documented GET routes respond |
| View exists | ✅ |
| Layout integrated | ✅ workspace pages render `_WorkspaceLayout` / `_SupplierLayout` |
| Owner comment present | ✅ every generated view, Identity page and public page |
| Authorization scaffold | ✅ area controllers `[Authorize(Roles = SystemRoles.X)]`, shared pages `[Authorize]` |
| Business logic | ❌ not implemented — no database access, all data-changing controls disabled |

Dashboards reuse the supplied layout's dashboard bodies. They show `—` / "Not connected" because no data is bound.

## How it was verified

1. `dotnet build` of the solution — **0 warnings, 0 errors**.
2. A temporary test host (not committed) ran the real application with a test-only sign-in that can claim roles:
   - all **89** routes in ROUTE_MAP rendered (HTTP 200) with the correct layout;
   - every link and asset found on those pages (**124** targets: navigation, breadcrumbs, related links, CSS, JS,
     images, `team.json`) returned 200;
   - anonymous requests to workspace and shared pages were redirected to sign-in;
   - a Supplier opening an Admin page was refused (403);
   - a Department user on a shared page got the Department sidebar; a Supplier got the supplier layout;
   - an unknown URL returned 404 with the friendly page.
3. Pages were rendered in headless Chrome at 1440 px (dark and light theme) and 390 px. No horizontal page scroll
   at 390 px on workspace pages.

## Known limitations

- **Roles are not seeded**, so with a real account every workspace page currently shows *Access denied*. This is
  correct behavior until Ahmad seeds the roles.
- `/Identity/Account/Manage` and the other non-scaffolded Identity pages keep the default Identity UI markup,
  styled by `identity.css`; they were not visually reviewed in depth.
- No email sender is configured: registration confirmation uses Identity's development confirmation link and
  password reset emails are not sent.
- The contact form endpoint answers *501 Not Implemented* on purpose.
- Trello card URLs are not in the repository; only "Create Tender" has one.
