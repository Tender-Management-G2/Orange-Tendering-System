# Global cross-filtering / state propagation

| Filter | Overview | Suppliers | Tenders | Forensics | AI Studio |
|---|---|---|---|---|---|
| Authorized organization/role scope | mandatory | mandatory | mandatory | mandatory | mandatory |
| Date range | reporting window | participation/evidence window | milestones/tenders window | findings period | inherited conversation context |
| Department | authorized dept aggregates | supplier participation in dept tenders | tender owner dept | evidence in permitted tenders | request tool params validated |
| Category | — | primary classification | optional tender item/group filter if meaningful | evidence entity category | only if current question implies it |
| Supplier(s) | optional shortcut | search/focus/compare | optional offer participation filter | affected entities filter | selection context |
| Tender and round | optional deep link | optional subgraph context | **primary selection** | related case | selected target |
| Relationship / detector | — | link overlay only | — | finding filter | tool allowed scope |
| Sealed/open bid state | Never include sealed amounts | Never include sealed amounts | Never include sealed amounts | Never include sealed amounts | Never send sealed values |

### Interaction contract
A filter change triggers a **single** coherent state update: metrics, graph nodes/edges, charts, table rows, evidence results, generated exports and AI context align to the exact same authorized query. Values whose denominator differs (registered supplier lifetime count vs bids in a date window) must state that distinction. Reset action restores scope, not authorization.

A chart click should expose active filter chips; back/forward should behave predictably. Drill-down selection can be ephemeral but preserved per tab. Avoid rendering all links on load; choose one relationship overlay or zero by default.
