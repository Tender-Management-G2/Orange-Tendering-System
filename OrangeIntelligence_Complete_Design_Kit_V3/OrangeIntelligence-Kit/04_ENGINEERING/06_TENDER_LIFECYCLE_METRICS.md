# Lifecycle KPIs — operational definitions and missing timestamps

Official exemplar: duration from **Tender created** to **winner notification sent**. Use actual event timestamps. Do NOT calculate from close date or the status label. `TenderCreatedAt` should be actual row creation/audit event; `WinnerNotificationSentAt` should be actual sent event, not email queued / approved / rendered. Distinguish delivered time if only that exists.

Calculations if both events present:
```
DurationSeconds = (NotificationSentUTC - TenderCreatedUTC).TotalSeconds
```
Use appropriate time zone for display only; maintain UTC as stored/normalized. If notification never sent, show `In progress` not a completed duration; if history unavailable show `Not recorded`.

Breakdown stages only where recorded: draft/preparation, invitation, bidding open/close, evaluation, manager review, top committee/final award, notification. Evaluation roles may see different details from Admin/Department. Median vs mean label explicitly. Avoid aggregating incomparable canceled/rejected/ongoing tenders as completed. Tender selection view should include a timestamp provenance tooltip.

Source checks Claude must perform: enumerate actual tender / rounds / invites / offers / evaluations / final award / notification and audit tables/fields; identify event timestamps; implement read model with the current schema; propose only minimal schema extensions if essential and request approval before migrations.
