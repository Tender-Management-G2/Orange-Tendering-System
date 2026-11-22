# Workv6 integration

Workv6 starts from Workv5 commit `abec812`, which includes the local Orange Intelligence and Radar updates pushed to `origin/Workv5` on 22 November 2026. Workv5 remains the source of truth for page structure, routes, and workflow sequence.

## Branches integrated

| Source | Head reviewed | Included in Workv6 |
| --- | --- | --- |
| `origin/tariq6` | `284da97` | Evaluation workflow, migrations, and role-scoped procurement assistant changes. |
| `origin/amal4` | `d5aa34c` | Homepage motion and workflow presentation refinements, plus shared changes from tariq6. |
| `origin/haya` | `38d305a` | Admin page styling, voice search, and the latest report view and export improvements, adapted to the existing `HayaAdminReports` route. |

## Conflict decisions

- Kept Workv5's `Analytics` controller, report model, report service, report views, and launch profiles. Haya's branch still used an older report architecture that would replace the current Amal analytics flow.
- Applied Haya's latest report page changes to `Views/HayaAdminReports` and export changes to `HayaReportExport` so both report implementations remain usable.
- Kept Workv5's connection settings. Excluded the generated `.codex-seed-run` DLLs from Haya's branch.
- Kept the Workv5 admin navigation, including Orange Intelligence, Analytics, and Haya Admin Reports, while accepting the compatible admin page design updates.

## Verification

- `dotnet build Orange-Tendering-System.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/CodexWorkv6Check/` succeeded. A pre-existing warning remains in `Areas/TopCommittee/Controllers/DecisionsController.cs`.
- `Tests/RadarChecks` passed 97 in-memory checks.
- `Tests/AuthSmoke --ai-agent-intent-checks` passed 23 intent cases and its category/embedding follow-ups.
- `node --check` passed for the changed `global-search.js`, `executive-agent.js`, and `theme-init.js` files.
- Database-backed workflow and browser checks were not run because they require a separate `OrangeAuthCheck_*` database and a running test host.
