# Local tools (optional)

Use your existing browser/test tooling first. These scripts are not required to start.

- `Preflight.ps1 -RepoPath <repo>`: reads Git/runtime/model availability and listening ports. Does not start/stop app or connect to DB. Run from PowerShell, not CMD. Review the output locally; no need to paste sensitive paths externally.
- `Install-Skills.ps1 -RepoPath <repo>`: copies original kit skills into project `.claude/skills` only if all target folders are absent. Does not overwrite or auto-commit. Review repository policy; if you prefer not to modify repo metadata, simply ask Claude to read the kit directly.
- `capture-radar.mjs`: optional Playwright capture helper, requires `playwright` and installed browser in a local JS workspace, an existing authorized DEMO storage state, real root selector/readiness, and explicit `--demo-confirmed`. Copy script into the existing test workspace or use its existing equivalent. Do not install dependencies automatically or export credentials; keep auth state and screenshots outside Git, use synthetic demo only. `--dev-https` allows self-signed loopback development HTTPS for this browser context only. Browser colorScheme may not set the app theme; verify real theme. The script does not click graph or voice controls; use acceptance matrix for those. Verify actual output locally; not executed in this kit environment.

Example:
```
node capture-radar.mjs https://localhost:7182/SourcingOfficer/MarketIntelligence ./private-demo-state.json ./private-artifacts --demo-confirmed --dev-https
```

Do not pass a real team session to this demo script. Existing repository tests are the authoritative functional tests. None of the scripts changes procurement records.
