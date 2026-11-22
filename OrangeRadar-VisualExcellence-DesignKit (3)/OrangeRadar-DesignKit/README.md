# Orange Radar — Visual Excellence Design Kit v2

**Purpose:** redesign the already implemented `/SourcingOfficer/MarketIntelligence` page. Not a new app. This handoff is an original project-specific set of instructions, references and optional code utilities, not a third-party plugin distribution.

## Start here
1. Extract the ZIP to an accessible folder, preferably outside the repository (for example `C:\Users\user\Desktop\OrangeRadar-DesignKit`).
2. Resume the original Claude Code session in the correct worktree. Verify actual repository, branch and database profile. Last reported: `Orange-Tendering-System-FinalVersion`, branch `Ahmad2`. Do not assume this is still true.
3. Paste `MASTER-PROMPT.md` and replace `[KIT_PATH]` with the actual absolute path to this extracted folder. Claude must read the files itself. If Claude cannot access the folder, authorize that directory via normal Claude Code folder permissions, or place the kit in the worktree without committing it.
4. Optionally install the six custom project skills using `tools/Install-Skills.ps1` from PowerShell; it never overwrites an existing skill. It is not necessary if Claude reads the kit directly. If installed skills are not picked up in a running session, resume/reopen Claude and invoke `/orange-radar-orchestrator`. The master prompt works independently of skill discovery.
5. Read `specs/00-BASELINE.md` and `specs/06-ACCEPTANCE.md` first. Code samples are optional, not drop-in replacements. No extra AI model downloads required.

## Contents
- MASTER-PROMPT.md — executable handoff to Claude Code.
- specs/ — source of truth for page, graph, visual language, voice, motion, acceptance and technical integration.
- references/images/ — seven selected visual references plus an index explaining their limitations.
- .claude/skills/ — six original Claude Code skill definitions tailored to this repository.
- starter/ — scoped CSS tokens and standalone pure-JS graph semantics helper with fixtures/tests, and optional audio-level utility.
- tools/ — safe local preflight, skill installer and browser screenshot helper.
- references/TECHNICAL-SOURCES.md — official technical documentation links.

## What is not included
No existing repository source, production data, credentials, executable browser binaries, model weights, or unreviewed third-party skills. No runtime package installation is performed by this kit. The actual redesign must be performed and verified locally by Claude. Images are static references, not working interfaces. The included JavaScript unit tests validate example helpers only, not the actual procurement app. PowerShell and browser scripts require local verification.
