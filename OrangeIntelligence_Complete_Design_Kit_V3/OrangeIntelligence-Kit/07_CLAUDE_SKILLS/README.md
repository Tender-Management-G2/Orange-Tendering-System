# Local Claude Code skills

8 narrowly scoped original project Skills in `.claude/skills/<skill-name>/SKILL.md`; not official packaged Anthropic skills. Installing them is OPTIONAL. They do not grant new OS tools or model access. `08_TOOLS/Install-Skills.ps1` prints proposed copies by default and requires `-Apply` to write into the repository. Always inspect any existing skills for conflicts before choosing to install.

Invoke when relevant through Claude Code or simply ask Claude to read the corresponding SKILL.md. Do not blindly dump all skills into the active conversation at once if tokens are expensive; use `01` + one target and `08` QA per pass.
