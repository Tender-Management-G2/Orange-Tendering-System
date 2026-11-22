# Mandatory AI Intelligence traceability matrix

**Status at kit creation: unverified.** Inspect repository and global chatbot before changing anything. Existing Orange Radar features may already satisfy some rows; do not duplicate them. Implemented means working at its authorized endpoint, not that a screenshot looks correct.

| ID | Requirement | Visible acceptance | Code/API evidence to inspect | Status now |
|---|---|---|---|---|
| AI-01 | Standalone page for all internal roles, not suppliers | Internal role navigation & direct URLs work; supplier denied; data scoped | Layout menu, authorize policies, controller routes | UNVERIFIED |
| AI-02a | Charts/Tables/Cards/KPIs | A visible and usable chart/table/card with consistent scope | Existing ECharts and reporting services | UNVERIFIED |
| AI-02b | Tender duration KPI | Duration from actual creation timestamp to sent winner notification; missing timestamps => N/A | Tender, notifications / email audit timestamps | UNVERIFIED |
| AI-02c | Excel and PDF upload/download | Correct type validation, preview/extraction, export, download permission | Existing upload, ClosedXML/EPPlus/PDF exporter | UNVERIFIED |
| AI-03 | Real data integration | Changing authorized data updates page; no mock numbers | Query/service scopes, cache invalidation | UNVERIFIED |
| AI-04 | Any authorized tender deep dive | Tender selector + timeline + bids + outcomes + evidence | Tender dashboard/report and authorization | UNVERIFIED |
| AI-05 | System-wide in-page chatbot | Chatbot available in pages; tender/supplier questions answered from data | Existing chat launcher/agent/tools | UNVERIFIED |
| AI-06a | Rich replies in chat | Charts, tables, cards *inside conversation*, not just navigating to dashboard | Typed UI message schema/renderer | UNVERIFIED |
| AI-06b | PDF/Excel intelligence | User uploads; extraction; safe analysis and export | Upload/extraction services | UNVERIFIED |
| AI-07 | Custom comparisons | AI creates a new chart/table/card from authorized dataset | Allowlisted chart spec and tool output | UNVERIFIED |
| AI-08 | Recommendations only | Human-readable recommendations, zero unauthorized workflow writes | Tool registries, save/decision endpoints | UNVERIFIED |
| AI-09 | Secure AI | Server-side role/department/company/tender checks for queries, exports, files, actions | policies + tests + service predicates | UNVERIFIED |
| AI-10 | Real facts | Numbers have verified supporting records; insufficient data is shown honestly | DTO provenance/claim checker | UNVERIFIED |

Extra **visual target** (not substitutes for AI-01..AI-10): 3D Suppliers Universe, voice orb, collapsible sidebar, scene transitions, five exclusive tabs, responsive and reduced-motion support.
