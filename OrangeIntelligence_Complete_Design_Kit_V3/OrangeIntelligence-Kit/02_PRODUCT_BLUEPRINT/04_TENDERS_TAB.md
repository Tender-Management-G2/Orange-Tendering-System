# Tenders — every authorized tender, clear lifecycle

Views: `All Tenders` (small list/summary) and `Tender Deep Dive` (one selected tender). Search/select any *authorized* tender; optional compare two or more. Do not show the Supplier Universe here.

For selected tender:
- Heading: ID, title, status, round, department, description.
- KPI quartet: invited count, eligible submitted offers, current lifecycle duration, official result status; choose indicators appropriate for stage; sealed bids never exposed.
- Timeline: creation → supplier invitation → bidding close → compliance → evaluation → approvals → final official award → winner notification **sent**; actual DB timestamps only; missing milestones labeled `Not recorded`.
- Bid comparison: only comparable currencies, items, quantity, evaluation access. Lowest/highest/median by fair basis; explain exclusions. Recommendations don't count as official awards.
- Scoped AI insights: evidence, status, results, delay causes only where record-backed.
- Export as authorized PDF/Excel; ensure selected tender context appears in file.

Do not invent lifecycle timestamps. If a required timestamp isn't stored, add a safe read-model from existing audit/notification tables or mark as missing and propose narrow future change, not a migration before user approval.
