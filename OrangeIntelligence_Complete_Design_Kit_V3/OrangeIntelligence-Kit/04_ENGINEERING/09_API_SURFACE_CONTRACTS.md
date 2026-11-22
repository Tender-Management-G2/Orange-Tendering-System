# Contracts — discover names in repo before coding

The following DTOs are **suggested** shapes, NOT claims about the existing schema. Reuse services and endpoint conventions where possible.

```
GetIntelligenceScope(): {roleCapabilities, authorizedDepartmentIds, dateBounds}
GetOverview(scope): {kpis[], tenderLifecycleSummary[], signals[]}
GetSupplierMarket(scope, filters): {kpis[], categories[], responseStats[], suppliers[], relationshipCounts}
GetSupplierGraph(scope, mode, selectedIds, relationFilter): {nodes[], links[], evidenceRefs[]}
GetSupplierDetails(id, scope): {profile, participation, pricePosition, awards, documentsAllowed, relatedFindings}
GetTenderList(scope, search): {id, title, stage, department, ...}[]
GetTenderDeepDive(id, scope): {summary, milestones[], bidComparisons[], officialOutcome, provenance}
GetForensicFindings(scope, filters): {list[], evidenceGraph, sampleSizes, alternativeExplanations}
AskIntelligenceAssistant(message, scopeContext): {text, visuals[], provenance, uiIntents[]}
AnalyzeUpload(sessionFileId, scope): {preview, sheetsOrPages, citations, warnings}
ExportIntelligence(selectionId, format, scope): authorized streamed file
```

All endpoint/tool arguments are **server validated**, not trusted from browser. Keep read-only transactions; no unbounded LLM SQL generation; use parameterized EF Core queries. Decouple query results from visual scene; the same DTO powers charts, tables and exports. Use anti-forgery on post forms and file upload, existing authentication/cookie and CSP.
