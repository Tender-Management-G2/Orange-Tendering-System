# System audit and integration - 13 November 2026

Audit method: a crawler visited every reachable page for each of the 7 roles (240 pages) and flagged HTTP errors, JavaScript errors, "not implemented / scaffold / not connected" text and access-denied dead ends.

## Found and fixed
| Problem | Fix |
| --- | --- |
| **Reports & Analytics was a scaffold** (6 pages with no data, Export buttons disabled; Top Committee statistics "not connected") | `ReportingService` computes every report from live SQL data; Excel (.xlsx) and PDF export of exactly what is shown; role-based audience (Admin/Officer/Manager/Top; evaluations not for Admin; management statistics for Top only). |
| Public home page statistics "NOT CONNECTED" | Real counts (active tenders, approved suppliers, completed requests, status donut, tenders per month). |
| **Department could not create a new purchase request** ("The Version field is required") | `RequestForm.Version` made nullable. |
| Officer's request decision did not notify the Department and left an orphan Pending review row | Decision completes the Pending review, notifies the requester and writes an audit entry. |
| Tender form required pasting Identity user ids for the four committee seats | Pickers listing only active users of the right role (and accepted purchase requests). |
| Admin opening Clarifications/New gave HTTP 500; Top Committee /Decisions gave 404 | Graceful message / redirect. |
| Table kit re-ordered editable item grids | Editable tables are skipped. |

## Verified (isolated SQL Server database, real app)
- Hand-off chain through the real UI: Department submits request -> Officer bell updates live -> Officer accepts -> Department sees Accepted + notification -> Officer creates the tender (four seats) -> Manager and committee members see it and are notified when the weight request starts -> suppliers cannot see it -> reports show it: `tests/flow_test.py` 16/16.
- Reports, exports, public statistics and role access: `tests/reports_test.py` 56/56 (Excel/PDF files validated, numbers equal SQL).
- Full page crawl/sweep: 95 pages, no errors. Final Decision + AI chat suite and clarification suite re-run: pass (the one SMTP-status check expects `Failed` because main configures a dev SMTP at 127.0.0.1:1025 with no listener).
- Release build 0 warnings / 0 errors; auth harness 211 checks.

## Still not done / needs configuration
- Face recognition login stays disabled (security decision from the biometric review).
- Live SMTP / Zoom credentials are not configured here; Ollama (`gemma3:4b`) must be running for AI features.
- Signing, scoring, Top Committee voting and award were not re-driven through the browser in this audit (existing timed lifecycle tests in `docs/test-evidence`).
