# Shared shell and navigation

### Persistent shell (not repeated per tab)
- Collapsible existing app sidebar; icon-only state, full state, keyboard shortcut only if it does not conflict with current site; persisted user preference; no data lost when expanded.
- Product logo and heading (Orange Intelligence); top tabs exactly one selected.
- Global filter **date window**, **department/authorized scope**; global search for authorized suppliers and tenders.
- Context status with active filter chips and `Clear filters`. All changes update chart/table/report scope consistently.
- **One voice session and context** accessible via a small, unobtrusive dock; expands in Suppliers or AI Studio. Dock must not cover axes, chart labels, tooltips, inputs, or buttons.
- Full keyboard navigation, focus management, Back-to-Universe and breadcrumbs for drill-down, preserve filters when appropriate.
- Theme toggle Dark/Light and Arabic/English. RTL layouts use logical CSS properties; numeric charts stay legible and series order intentional.

### Tab interaction
Clicking a tab changes the rendered view, not scrolling to another full-page section. A tab may scroll inside its own long content (one natural page scrollbar) where needed. On switch, preserve global date/department and appropriate per-tab filter state without accidentally applying supplier-only filters to tender-level statistics.

### No duplication budget
- Each KPI has **one canonical owning tab**; Overview can contain a short summary/link only.
- One general supplier network owner: Suppliers.
- One full forensic evidence explorer owner: Forensics.
- One full custom chat canvas owner: AI Studio.
- Shared voice dock is a *singleton*, not one component instance per tab.
- Export is a contextual action, no detached Reports tab.
