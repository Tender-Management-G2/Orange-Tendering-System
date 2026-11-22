# Orange Intelligence — New End-to-End Design Kit

This is a clean redesign kit, not a patch to the prior Orange Radar ZIP. It is a design/engineering brief for Claude Code, *not* a completed application. It includes user-provided original sources, screen-specific visual concepts, eight project-local Claude Code skills, interface contracts, CSS design tokens, small sanity-check tools, and a tightly scoped acceptance rubric.

**Primary design rule:** exactly ONE active top-level tab renders its content. `Overview`, `Suppliers`, `Tenders`, `Forensics`, `AI Studio` are five perspectives of one authorized intelligence workspace. Do NOT stack all five inside a single vertical page. Each individual tab can scroll naturally, and a shared voice session persists across navigation.

Scope only AI Intelligence requirement #1–#10 from the official PDF; other PDF requirements are out of scope. Preserve existing application code and services after inspecting the repository. Do not assume any file paths, database schemas, branch names, or prior claims are correct without verification. Do not commit or push.

Start at `MASTER_PROMPT.md`, then `01_REQUIREMENTS/REQUIREMENTS_MATRIX.md` and `02_PRODUCT_BLUEPRINT/00_NORTH_STAR.md`. Images are design references only: do not copy text/numeric examples into the application.
