# Graph data contract and evidence semantics

Use canonical IDs (not display names) and privacy-safe field selection. Example **illustrative shape**:

```json
{
  "scope": {"dateFrom":"2026-01-01","dateTo":"2026-12-31","departmentId":null},
  "suppliers": [{"id":42,"displayName":"Supplier A","categoryKey":"it","approved":true,"participationClosedRounds":4}],
  "edges": [{"sourceSupplierId":42,"targetSupplierId":51,"kind":"joint_closed_round","sharedRoundIds":[15,18],"strength":2,"explanation":"Both submitted offers in two closed rounds","evidenceIds":["round-15","round-18"]}],
  "findings": [{"id":"F1","kind":"document_exact_match","documentHashPrefix":"48af...","supplierIds":[42,51],"sourceRecordIds":["file-6","file-7"],"status":"review_signal"}]
}
```

Only values that exist in actual authorized records may be served. `sharedRoundIds` and evidence links are server-filtered; do not leak sealed bid totals or competitor-private records. Never send unredacted document hashes or private contact details to model if not necessary; usually a safe hash prefix/count suffices. Network count semantics: total node count = visible supplier count; relationships count = distinct semantic edge records, not number of animated strokes or graph particles.

Semantic modes:
- **Category**: disjoint categories based on registered business type (not evidence of collusion).
- **Competition**: repeated co-bidding in shared closed rounds, context only, not a risk finding.
- **Tender**: `Tender/Round → Supplier` bipartite representation when user selects tender context.
- **Forensic**: `Finding/Evidence → Entity` graph with clear documented trigger, never accusation.

Validation rules: unique suppliers by ID; deterministic edge IDs based on canonical semantic relationship; counts computed before animation; no duplicate edges; no negative counts; missing data labeled missing. Default graph can have no edges if filter is `None`.
