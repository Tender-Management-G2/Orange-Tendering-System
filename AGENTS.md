# AGENTS.md — repository rules for every agent

## Current workspace (owner update, 2026-11-27)

- Repository worktree: `C:\Users\user\Desktop\Orange\Projects\Orange-Tendering\Orange-Tendering-System-FinalVersion`
- Current integration and development branch: `Workv7-from-Workv6` (case-sensitive)
- Remote branch: `origin/Workv7-from-Workv6`
- Visual Studio solution: `Orange-Tendering-System.slnx` at the worktree root
- Web project: `Orange-Tendering-System/Orange-Tendering-System.csproj`
- Application URLs: `http://localhost:5254` and `https://localhost:7182`, as defined in `Orange-Tendering-System/Properties/launchSettings.json`

This replaces the 2026-11-16 workspace instruction that named `C:\Users\user\Desktop\Orange-Tendering-System`, `tariq22222`, and `WorkV2`. That directory is not the current worktree. Use `Workv7-from-Workv6` for continuing this integration unless the owner explicitly selects another branch or worktree.

## Check before editing, building, committing, or pushing

```powershell
git rev-parse --show-toplevel   # expect the worktree path above
git branch --show-current       # expect Workv7-from-Workv6 unless the owner directed another branch
git status --short              # inspect existing local changes
git worktree list               # identify other active worktrees
```

If the root or branch differs from the intended task, stop and report it before writing. Do not silently switch branches or edit a different worktree. Preserve uncommitted changes already present. If another agent is actively editing the same worktree, coordinate before touching overlapping files.

## Working and publishing rules

- Build and test from this worktree. Keep test data isolated from the working application database.
- Commit only intended source and documentation. Never commit User Secrets, SMTP credentials, biometric data, OTP codes, personal settings, test databases, build output, `.codex-seed-run/`, or `node_modules`.
- Push changes to this branch with `git push origin Workv7-from-Workv6`. Never force-push. Push to another branch only when the owner explicitly requests it.
- Before integrating another branch, fetch its current remote head and compare the relevant files and rendered behavior. An ancestor relationship alone does not prove that later commits preserved a page or feature.
- Other worktrees and old copies remain independent. Do not reset, clean, delete, or overwrite them as part of Workv7-from-Workv6 work.

## Running the application

Use Visual Studio with `Orange-Tendering-System.slnx`, or from this worktree root:

```powershell
dotnet run --project Orange-Tendering-System/Orange-Tendering-System.csproj --launch-profile https
```

Check that ports 5254 and 7182 are available before starting another copy. `scripts/Run-WorkV2.ps1` still checks the obsolete path and `tariq22222`; do not use that script for Workv7-from-Workv6 unless it is updated separately.

## Project references

- Domain and workflow rules: `TenderingOrangeSystem.md` and root `docs/`.
- Authentication: `docs/BIOMETRIC_SIGNIN.md`.
- Workv5 historical integration sources, conflicts, file inventory, and the Evaluation Committee follow-up: `Orange-Tendering-System/docs/WORKV5_MERGE_REPORT.md`.
- Current report implementations: Amal analytics under `/Analytics`; Haya admin report pages under `/HayaAdminReports`.
