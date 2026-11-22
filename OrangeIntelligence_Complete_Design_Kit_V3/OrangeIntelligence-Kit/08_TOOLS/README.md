# Local utilities included — no proprietary binaries

- `Preflight.ps1`: read-only Windows/PowerShell check for correct repo, dotnet, Git, Ollama, Piper folder, GPU; does not run the app or migrate DB.
- `Install-Skills.ps1`: safe dry-run; `-Apply` only creates missing project skills. Does not overwrite existing skills.
- `verify_kit.py`: checks kit files/skills/images.
- `graph_semantics.mjs` + `graph_semantics.test.mjs`: small self-contained reference for distinct **closed-round** joint participation links; `node --test .\08_TOOLS\graph_semantics.test.mjs`. Demonstrates avoiding duplicate/open/sealed links but not a substitute for actual EF domain logic.
- `ai_ui_allowlist.schema.json`: minimal sample contract for safe declarative generated charts.
- `sample_only_NOT_REAL_DATA.json`: explicitly fictional sample, never seed into team DB.

No model weights or downloaded JS libraries are bundled. Use existing `wwwroot/lib` where practical. This package is designed to be read by Claude Code in repo; do not place under the deployed web root.
