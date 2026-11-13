# Top Committee reporting implementation

All source changes are confined to `Areas/TopCommittee`. Both pages use the existing `_WorkspaceLayout` through the Area's unchanged `_ViewStart.cshtml`. Shared Navbar, Sidebar, CSS, authentication configuration, models, enums, DbContext and migrations are unchanged. No database records were created or updated.

## Pages and actions

- `/TopCommittee/Reports` — Reports & Analytics – Top Committee.
- `/Analytics` — role-constrained alias serving the same Reports view to Top Committee users, allowing the existing Sidebar link to work without editing shared navigation. Other roles retain the shared route.
- `/TopCommittee/Statistics` and `/TopCommittee/Statistics/FullSystem` — Full System Statistics.
- Each controller has `ExportExcel` and `ExportPdf` actions for its own report.
- `/TopCommittee/Statistics/Management` preserves the earlier supplier award/currency details. The existing `Export` action remains available for that report.
- Both controllers require the existing `Top Committee` role and an active account. Anonymous requests are challenged through existing Identity login. All reads and new downloads use no-store response caching.

## Created files

- Controllers/ReportsController.cs
- Controllers/TopCommitteeReportsRouteAttribute.cs
- Services/TopCommitteeReportingService.cs
- Services/ReportingWorkbook.cs
- Services/ReportingExport.cs
- Services/BrowserReportPdf.cs
- ViewModels/ReportingModels.cs
- Views/Reports/Index.cshtml
- Views/Reports/Pdf.cshtml
- Views/Statistics/System.cshtml
- Views/Shared/_ReportingContent.cshtml
- Views/Shared/_ReportStyles.cshtml
- Views/Shared/_ReportIcon.cshtml
- Views/Shared/_ReportDonut.cshtml
- Views/Shared/_ReportBars.cshtml
- Views/Shared/_ReportLine.cshtml
- .gitignore (temporary verification and export workspaces)
- ReportingImplementation.md (this report)

## Modified files

- Controllers/StatisticsController.cs — new statistics page and page-specific export actions; previous management functionality retained.
- Services/StatisticsWorkbook.cs — reusable OpenXML writer, Orange header styling, wrapped cells, numeric formats, frozen header row.
- Services/TopCommitteeCompatibility.cs — tolerate legacy numbered Top member responsibility strings as null in the Top-specific EF model only. Stored data and the shared EF model remain unchanged; no evaluation report is promoted to an executive summary.
- .checks/ReadChecks.cs — read-only regression checks updated to current data rather than assuming specific manually inserted records exist.

## ViewModels and rendering

`ReportingPage` selects the report presentation; `ReportingData` holds totals, distributions, suppliers, six monthly buckets and summary rows. `ReportPoint`, `ReportChart`, `SupplierPerformance`, `ActivityMonth`, `ReportSummaryRow` and `ReportFormat` provide typed, zero-safe rendering. Charts render as SVG or CSS with real server-side counts, without adding shared scripts. Main content is responsive; there are no filter controls, dropdowns or horizontal tabs.

## Database metrics

| Metric | Existing tables and meaning |
|---|---|
| Total Suppliers | Suppliers count, one company per row |
| Total Tenders | Tenders count |
| Awarded Contracts | Tenders with Status Awarded; the schema has no separate contract entity |
| Purchase Requests | PurchaseRequests count |
| Completed Votes | CommitteeVotes joined to its Top CommitteeRound and Top CommitteeMember on the same tender; Approve or Reject records, not completed committee rounds |
| Tender status | Tenders.Status; Open = nonterminal stages including Draft and review; Closed = Rejected + Cancelled; Awarded separate |
| Request status | PurchaseRequests.Status; Draft, Submitted, Under Review, Accepted, Rejected, Returned. Legacy Approved is displayed as Accepted locally. Converted to Tender is also shown when present so totals reconcile |
| Supplier transactions | TenderInvitations with any related Bids.SubmittedAt; one invitation per tender round, revisions count once |
| Supplier awards | Awarded Tenders whose AwardedBid links through Bids and TenderInvitations to that Supplier |
| Success rate | Awards / submitted-offer transactions; unavailable for zero or inconsistent denominators. Top 5 ranked by awards, transactions, then name |
| Participation | Invitations with a submitted offer / all TenderInvitations; unavailable without invitations |
| Zero bids | Tenders without a submitted offer across all TenderRounds and TenderInvitations |
| Evaluation duration | Completed Evaluation CommitteeRounds with valid StartedAt and CompletedAt; mean elapsed days |
| Request approval duration | PurchaseRequestReviews: each request's latest Accepted submission with valid SubmittedAt and ReviewedAt; mean elapsed days |
| Supplier statuses | Suppliers.Status: Registered = all company records; Approved and Suspended are subsets. Login account activation is not company registration status |
| Total Bids | Bids count, including separately recorded revisions |
| Total Evaluations | CommitteeRounds with CommitteeType Evaluation |
| Monthly activity | Last six UTC calendar months, including partial current month: PurchaseRequests/Tenders/Bids.CreatedAt and Evaluation CommitteeRounds.StartedAt |
| Summary | Accepted/request total, Awarded/tender total, Approved/supplier total, bid records/tenders, completed evaluation rounds/evaluation rounds |

## Exports

Excel downloads are genuine `.xlsx` OpenXML workbooks, with seven sheets per report containing overview, relevant chart/table data, and definitions. Text cells are inline strings, never executable formulas. PDF uses the same report-only Razor content, inline styles, and installed Microsoft Edge in isolated headless mode. The PDF does not include the application's navigation or authenticated cookie. Temporary renderer jobs are confined to `.exports` inside this Area and cleaned after use.

PDF hosting requirement: Microsoft Edge must be installed on the Windows host and executable by the application identity. If unavailable, export reports a 503 error; it never returns HTML disguised as PDF. No package, Program.cs or shared service-registration changes were needed.

## Missing fields and interpretation

The schema does not store an evaluation SLA/planned completion deadline, so true overdue-versus-target evaluation delay cannot be calculated. The page explicitly identifies its displayed value as elapsed evaluation time. Missing valid timestamps and zero percentage denominators display an em dash. No growth percentages are fabricated. Current stored data can legitimately produce empty supplier charts and zero voting/evaluation totals. Converted requests remain visible as an additional real workflow status.

## Verification

- Application build: succeeded with zero errors and zero warnings using Area-local intermediate/output directories.
- Eleven offline/SQL read-only checks passed: isolated compatibility model, canonical writes, legacy reading and user-scoped SQL, voting blockers, real totals, status reconciliation, monthly buckets and unchanged EF tracker.
- Existing authorized demo account logged in normally through Identity; both pages and the existing `/Analytics` Sidebar destination returned the correct content.
- Both PDF and both Excel endpoints returned real downloadable file bytes and correct MIME types.
- All six new page/export routes and `/Analytics` challenged anonymous access through the existing login page.
- Excel files reopened using openpyxl; sheet titles, Orange header fills and absence of formula cells verified.
- PDF files reopened, text checked, rendered to PNG and visually reviewed.
- Generated build output changes were removed from the source diff after verification. No shared source files changed.
