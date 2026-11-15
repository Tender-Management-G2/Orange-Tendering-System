# AGENTS.md — repository rules for every agent (Codex, Claude, others)

## Authoritative workspace (owner decision, 2026-11-15)

- Local repository: `C:\Users\user\Desktop\Orange\Projects\Orange-Tendering\Orange-Tendering-System-WorkV2`
- Development branch: `WorkV2`, tracking `origin/WorkV2`
- Visual Studio solution: `Orange-Tendering-System.slnx` in that folder
- Application ports: `http://localhost:5254` and `https://localhost:7182` (`Orange-Tendering-System/Properties/launchSettings.json`)

All development, builds, tests, commits and pushes happen here. Other folders (the original `Orange-Tendering-System`
folder on `Ahmad`, `WorkV1`, `Orange-Tendering-System-AuthIntegration`, earlier test copies) are preserved but inactive:
do not develop in them, delete them, reset them or discard their uncommitted changes.

## Before writing, building, committing or pushing

```powershell
git rev-parse --show-toplevel   # must be ...\Orange-Tendering-System-WorkV2
git branch --show-current       # must be WorkV2
git status --short              # know what is already uncommitted
```

If the root or branch is wrong, stop before writing any file and report the mismatch. Do not switch branches silently,
and do not create another clone, worktree or copy without the owner's explicit permission.

## Working rules

- Push only with `git push origin WorkV2`. Never force-push. Do not push to `main`, `WorkV1` or `Ahmad` unless the
  owner asks for that branch explicitly.
- One agent per working tree at a time. Before editing, check for recent file changes and running `codex.exe` /
  Claude sessions; if another agent is active in this folder, coordinate instead of editing the same files.
- Commit only intended project files. Never commit User Secrets, SMTP credentials, biometric data, OTP codes,
  personal settings (such as local port changes), test databases, build output or `node_modules`.
- Run the application from this folder only: Visual Studio with `Orange-Tendering-System.slnx`, or
  `powershell -ExecutionPolicy Bypass -File scripts\Run-WorkV2.ps1`, which refuses to start on the wrong folder,
  branch or an occupied port. Stop any other copy that holds 5254/7182 instead of moving to other ports.
- Project and domain rules live in `TenderingOrangeSystem.md` and `docs/`; authentication details in
  `docs/BIOMETRIC_SIGNIN.md`.
