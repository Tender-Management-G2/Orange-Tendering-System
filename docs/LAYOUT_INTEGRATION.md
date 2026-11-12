# Layout Integration

How the supplied layout ZIP (`OrangeSourcing-MVC10-EFCore (2) (1).zip`) was integrated. Owner: **Tariq — Shared Layout**.
Paths are relative to the project folder `Orange-Tendering-System/`.

## What the ZIP contained

A complete, *UI-only* ASP.NET Core MVC 10 project (`src/OrangeSourcing.Web`, its own `Program.cs` says
"no database, Identity, authentication"), plus test projects and their build output (most of the 132 MB).
Only the web project's views and static files were used. The tests, build output, its database script and its
`Program.cs` were not copied.

## Files reused

| From the ZIP | Placed at | Notes |
|---|---|---|
| `wwwroot/css/styles.css`, `premium.css`, `experience.css`, `mvc.css`, `public-pages.css` | `wwwroot/css/` | public site theme |
| `wwwroot/css/workspace-shell.css`, `workspace-dashboard.css` | `wwwroot/css/` | workspace shell + dashboards |
| `wwwroot/js/theme-init.js`, `experience.js`, `public-pages.js`, `app.js`, `sections.js`, `contact.js`, `editorial-team.js`, `workspace-shell.js`, `vendor/gsap.min.js`, `vendor/ScrollTrigger.min.js` | `wwwroot/js/` | unchanged |
| `wwwroot/assets/building.png`, `orange-internal-reference.png`, `orange-supplier-reference.png` | `wwwroot/assets/` | used by the CSS |
| `wwwroot/data/team.json` | `wwwroot/data/` | Team page content (still placeholders) |
| `Views/Shared/_Layout`, `_Header`, `_Footer`, `_Icons`, `_ContactForm` | `Views/Shared/` | links re-pointed to Identity |
| `Views/Home/Index`, `About`, `HowItWorks`, `Team`, `Contact` | `Views/Home/` | owner comments added |
| `Views/Dashboards/*.cshtml` (7) | `Areas/*/Views/Dashboard/Index.cshtml` | one per role |
| `ViewModels/TeamMemberViewModel`, `WorkspaceDashboardViewModel` | `ViewModels/` | namespace changed |
| `Models/SystemRoles.cs` | `Models/SystemRoles.cs` | now used by `[Authorize]` |
| `Views/Shared/_AdminLayout` … `_TopCommitteeLayout` (6 internal) | **merged** into `_WorkspaceLayout` + `_Sidebar` | see below |
| `Views/Shared/_SupplierLayout` | `Views/Shared/_SupplierLayout.cshtml` | menu now uses real links |

## Not used, and why

| ZIP file | Reason |
|---|---|
| `Pages/Account/Login`, `Views/Suppliers/Register`, `Controllers/AccountController`, `SuppliersController` | Replaced by the existing ASP.NET Core Identity pages (no second authentication system) |
| `Controllers/DashboardsController`, `Views/Dashboards/Index` | Public "preview" dashboards; the real dashboards are now protected area pages |
| `Pages/Status`, `Views/Account/AccessDenied` | UI-preview pages; Identity's AccessDenied is used |
| `wwwroot/js/mvc-integration.js` | Calls `/api/procurement/insights`, which does not exist here |
| `Services/ClarificationPolicy.cs`, `ViewModels/Forms.cs`, `Database/InitialCreate.sql`, `Security/` (empty) | Business logic / different schema — out of scope |

## Layout decisions

- **Three layouts, not nine.** The ZIP had six internal role layouts that were identical except for the role name
  and sidebar items. They became one `_WorkspaceLayout.cshtml`; the per-role menus live in
  `ViewModels/WorkspaceNavigation.cs` and are rendered by `_Sidebar.cshtml`. The Supplier layout stays separate
  because the ZIP gives suppliers a different structure (top navbar + footer, no sidebar). Public pages and
  Identity pages share `_Layout.cshtml`.
- Disabled "preview" menu buttons from the ZIP are now real links to the scaffolded pages.
- Search and voice search stay visible but disabled (voice search is an unassigned mandatory feature).
- Notifications bell links to the shared Notifications page; sign-out is the real Identity POST.

## Files added for the integration

`Views/Shared/_WorkspaceLayout`, `_SupplierLayout`, `_Sidebar`, `_WorkspaceAccountMenu`, `_PageHeader`,
`_ScaffoldNotice`, `_EmptyState`, `_ConfirmAction`, `_ApprovalTracker`; `wwwroot/css/workspace-pages.css`
(page components on the ZIP's `--ws-*` theme tokens) and `wwwroot/css/identity.css` (styles the default Identity
UI markup without Bootstrap).

## Existing files changed or removed

- `Views/Shared/_Layout.cshtml` — replaced by the ZIP's public layout (Bootstrap layout removed).
- `Views/Shared/_LoginPartial.cshtml` — ZIP styling, same Identity links.
- `Views/Shared/Error.cshtml`, `Views/Home/Privacy.cshtml` — restyled.
- Removed template files no longer referenced: `Views/Shared/_Layout.cshtml.css`, `wwwroot/css/site.css`, `wwwroot/js/site.js`.
- Preserved: `wwwroot/lib/*` (jQuery validation is still used by the Identity forms), `favicon.ico`.

## Fixes made to the supplied CSS behavior

All in `workspace-pages.css`, without editing the ZIP files:
- Long role menus covered the sidebar's building image → the image now sits below the menu.
- The sidebar brand was wider than the sidebar below ~1600 px → tighter spacing, no sidebar scrollbar.
- Wide tables could widen the page on phones → tables scroll inside their panel.

## Content corrections against the specification (audit, 13 Nov 2026)

Text only; the layout and styling are unchanged:
- `Views/Home/Index.cshtml` — the hero now states the required core message "Unified Platform for Sourcing &
  Tender Management" (spec §15.1); the statistics caption no longer says "Sign in for verified statistics", because
  public statistics are non-confidential counts, not login-only data.
- `Views/Home/HowItWorks.cshtml` — step 2 no longer says criteria are set at tender creation; the Evaluation
  Committee sets them (owner decision, spec §4.5).

## Open layout items

- `team.json` has 4 placeholder members; the team has 7 (Tariq, Team Page — Doing).
- Supplier footer "Terms of Use" has no content (shown as unavailable).
- The home page's role/communication explorers are the ZIP's illustrative demos, labelled as such.
