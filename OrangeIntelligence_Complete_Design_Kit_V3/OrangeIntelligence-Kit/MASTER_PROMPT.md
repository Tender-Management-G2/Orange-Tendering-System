# CLAUDE CODE — ORANGE INTELLIGENCE V3 / GREENFIELD VISUAL DESIGN, EXISTING APP INTEGRATION

You are implementing a **complete visual and information-architecture redesign of the existing AI Intelligence experience** in the existing Orange Sourcing & Tender Management System (ASP.NET Core MVC, EF Core / SQL Server). This is **NOT a new greenfield backend project**. Reuse all proven existing Radar, chatbot, analytics, voice and document work wherever possible.

## 0. Source-of-truth and mandatory reading

Read THIS prompt and these project kit documents, in order:

1. `00_START_HERE/README_EN.md`
2. `01_REQUIREMENTS/OFFICIAL_REQUIREMENTS_01_10_AR.md` **and the original PDF** `01_REQUIREMENTS/MVC Project - Updates (original).pdf` (AI requirements #1 through #10 only)
3. `01_REQUIREMENTS/REQUIREMENTS_MATRIX.md` and `SOURCE_PRIORITY_AND_GAPS.md`
4. `02_PRODUCT_BLUEPRINT/00_NORTH_STAR.md`, `01_PAGE_SHELL_AND_NAVIGATION.md`, and the active tab specification(s)
5. `03_VISUAL_REFERENCES/IMAGE_GUIDE.md` and reference images *for the current tab only*
6. `04_ENGINEERING` as relevant, plus `05_DESIGN_SYSTEM` and `06_ACCEPTANCE`.

The bundled `07_CLAUDE_SKILLS/.claude/skills` contains **8 original project skills**, each with a self-contained `SKILL.md`. Read the relevant skill(s); do not needlessly load every file into context before writing targeted code. Follow current repository `AGENTS.md` and user instructions if applicable.

## 1. Owner's FINAL information architecture decision (CRITICAL)

We repeatedly failed previously because you stacked the contents of multiple pages into one enormous dashboard. DO NOT DO THAT AGAIN.

There is ONE shared **Orange Intelligence** workspace with **EXACTLY FIVE EXCLUSIVE TOP-LEVEL TABS**:
- `Overview`: compact role-aware executive summary and shortcuts.
- `Suppliers`: supplier KPIs, a compact market snapshot, the breathtaking living **3D Supplier Universe** in the center, ONE persistent AI voice experience, supplier detail only on selection. This is the signature visual screen.
- `Tenders`: pick ANY authorized tender; view its data, lifecycle, bids, outcomes and report. No general supplier globe.
- `Forensics`: evidence-backed review findings with purpose-built evidence network/price/timeline visualization. Never accuse people or suppliers of fraud. No second general supplier globe.
- `AI Studio`: expanded global chat, custom generated charts/tables/cards **inside chat**, PDF/XLSX uploads and extraction, grounded reports and exports.

When a top-level tab is clicked, **replace the main content**. Do not just scroll to a different giant section or display Tenders/Forensics/Reports below Suppliers. Each tab can have its own thoughtfully spaced content and normal page scroll. There is **NO permanent Reports tab**: reporting/export/pinning are contextual actions. There is **ONE** shared AI conversation (including voice) across the workspace and existing in-page assistant where required.

## 2. Priority #1 — Suppliers screen visual quality

Use `01_Suppliers_Primary_Cinematic_Concept.png` for the atmosphere (globe, voice, layout, color/materials); use individual other PNGs for their respective screens, **not as elements to stack beneath Suppliers**. All numbers in pictures are illustrative and MUST NOT become real values, awards, relationships, confidence scores, or locations.

The main visual is an extraordinary, cinematic but highly readable 3D sphere of real authorized supplier nodes. Explore 3D Force Graph/Three.js **after evaluating existing Radar 2D/canvas and project compatibility**. It must genuinely auto-orbit slowly and continuously (unless pause/reduced motion), with subtle, bounded node movement, intuitive drag/zoom/focus, responsive elastic relationship highlighting and meaningful spatial transitions. No global fake `All Suppliers` node. No geography masquerading as company location. Users must easily find every supplier, even isolated ones. No hairball of orange lines: start with uncluttered view; relationship overlays filter precisely; evidence is accessed on selection. Stop orbit on selection; use stable readable focused layout with 2D/table fallback.

Large, accessible typography (body ~16px, chart/graph labels ~14–16px). No overlapped names/axes, microscopic legend, random halos, endless cards, cut-off tender names or unnecessary duplicate information. Network dominates; controls are compact; panel opens only when needed. Dark + Light + AR/EN + narrow viewport must remain usable. Collapse existing app sidebar to gain space.

## 3. Mandatory functional requirements, do not substitute visuals

Official requirement #1: shared AI Intelligence page accessible to **all internal roles except Supplier**, with strict per-user/per-department/tender access (not blanket data access). #2 interactive charts/tables/analysis cards/KPIs, **duration from tender creation to winner notification sent**, upload/download PDF and Excel. #3 real changing DB data. #4 select any authorized tender and render tender-specific interactive analysis. #5 system-wide embedded chatbot handling tender/bid/price/status/participation questions. #6 rich responses inside conversation plus file import/export. #7 arbitrary user-requested visual analytics and comparisons *inside chat*. #8 recommendations only: **no workflow state mutation or business decisions performed by this Intelligence assistant**. #9 server-side Secure AI. #10 no invented figures; use real authorized data with provenance.

Check existing global chatbot and Orange Radar before implementing a missing feature; the feature may already be elsewhere. Record `existing / adapt / truly missing` in concise notes. No fake demos in shared team DB, no leaks of sealed offers.

## 4. Implement design and integration pragmatically

Start by verifying branch/worktree/status/active app/database and existing interfaces; do not assume reports from earlier sessions remain true. No code modifications unrelated to this AI Intelligence surface except a safe common sidebar/assistant adaptation as needed. Prefer existing services to new tables/migrations, and NEVER run team-seeding jobs, migrations or emails without explicit approval.

Build a single coherent filter state shared by relevant KPIs/charts/network/AI/export. Data transformations are in trusted .NET/SQL; AI is an interpreter/tool chooser, NOT an arithmetic authority. Generative UI consumes an allowlisted, validated JSON component schema, NEVER raw AI-generated HTML/JS/SQL. Uploads remain isolated, source-attributed and untrusted. Keep existing local Qwen/Whisper/Piper; do not change models or add frameworks needlessly.

## 5. Practical, low-token implementation sequence

1. Inspection: quick path/role/feature inventory; identify current code, don't rerun every test.
2. Shell + exclusive tabs + global filters + collapsible sidebar + one AI session.
3. **Fully realize Suppliers visual**. Run browser at normal zoom and take one clear screenshot plus an 8–12-second animation clip. Iterate until visual meets north star. If time constrained, perfect Suppliers and preserve other working functions rather than generating mock-looking incomplete screens.
4. Tenders, Forensics, AI Studio, Overview using their own specific PNGs/specs; port existing functions rather than duplicate.
5. Minimal targeted tests: role deny and dataset scope, sealed offers, tender lifecycle timestamp, AI custom chart in chat, file export, motion pause / responsive / readable graph.
6. Final output at most ~20 lines: list changed files, screenshots/clip paths, verified checklist, unverified items/blockers. NO COMMIT, NO PUSH.

**Definition of done:** a beautiful understandable *working* app with five exclusive tabs, actual role-authorized data, real gentle 3D motion and meaningful interaction, one context-aware voice agent, tender drilldown, forensic evidence, and generated visuals in chat. A static screenshot, passing compiler or long test report is not enough. Do not claim features done merely because the kit describes them.

## Final instruction

Creativity is welcome and expected; use the references as art direction, not a layout to blindly screenshot. The user's central complaint is information overload, duplication and unreadable static graphs. Solve those issues while retaining mandatory requirements. Begin by inspecting the current repo and opening the specific relevant source files, then implement **Suppliers first**. Preserve uncommitted team work and leave everything uncommitted for owner review.
