# Unified Officer Tender flow

The Sourcing Officer sidebar starts with **Dashboard → Request → Tender**. The remaining entries keep their order.

- Request: `/SourcingOfficer/PurchaseRequests`. Every row opens its request details. Only submitted requests show decision controls. Return/reject require a reason. Accepted requests continue through the Tender navigation, without an inline creation form.
- Tender: `/SourcingOfficer/Workflow`. This is the existing weighted workflow, renamed, and is the canonical creation and management page. The create dialog selects an accepted request without a tender and assigns the four committee seats. Weight requests, proposals, manager approval, bidding, supplier invitations, offer receipt, evaluation, reports and final approvals stay in the existing workflow service.
- The creation service locks the purchase request and rejects a second tender for that request. Re-tendering uses rounds on the same tender.
- Old Tenders GET routes redirect to the canonical workflow. Old creation, invitation and next-round POST forms return 409 instead of bypassing setup. Their former views remain in the repository for reference but no longer drive requests.
- Received Bids remains available. Its compliance action delegates to the same workflow service. Its old direct committee-forwarding POST is retired; the GET opens the relevant Tender. Legacy report forwarding delegates to the current report-forwarding action and its signature/version checks.
- Supplier picking in Tender includes company search, category filtering and checkboxes. Only approved suppliers with an active verified account may be invited. The service re-checks eligibility and weight approval.

## Supplier confidentiality

Supplier workflow/details responses use `SupplierTenderPortalViewModel`, loaded through `SupplierTenderPortalService`. The response contract excludes committee members, weights, scores, rankings, internal stages and competitor offers.

Supplier lists show Open / Upcoming / Closed availability rather than evaluation or approval stages. Popups expose requirements, dates, quantities, published tender attachments, invitation responses and own offers. Existing final-result disclosure still depends on the Officer's published result notification.

Offer creation/submission keeps the existing accepted-invitation, approved-account, bidding-window and internal readiness gates. It is not gated by a predicted outcome, ranking or internal compliance flag. Supplier-facing readiness errors and new invitation notices do not describe confidential weight/committee processing. Officer clarification requests remain the channel for missing information.

## Existing records

The Officer may close bidding once at least three distinct suppliers have submitted offers in the current round, even while other invitations are pending. Drafts, acceptance alone and multiple revisions from the same supplier do not increase that count. The Close bidding button and server gate use this rule. Deadline/all-answered closure remains available for low-response rounds. Reaching three enables Officer closure; it does not automatically close the round during supplier submission.

No domain schema migration or automatic data reset was added. Historical tenders created by the old route may lack a weight round or committee setup. They are preserved and flagged to the Officer; invitations are blocked until a reviewed setup correction. This change does not silently approve weights or rewind live procurement records.

## Verification

`Tests/AuthSmoke --unified-tender-checks` exercises real Identity cookies, MVC endpoints and workflow services against a uniquely named local `OrangeAuthCheck_Unified_*` SQL Server database. It does not seed the working database or send SMTP invitations. The last run passed 47 checks, including request review, canonical creation, duplicate prevention, old-route rejection, committee/weight setup, category-picker rendering, supplier submission, confidential-response checks and cross-supplier access denial.

Local results: `App_Data/SajaDemo/unified-tender-checks.txt` (ignored by Git). Browser review also verified sidebar ordering, Request details links, the renamed Tender page and the supplier-only popup. Later meeting/scoring/signature/final-award transitions were not rerun end to end in this change.

Known existing issue: an empty antiforgery 400 POST can be re-executed against the GET-only status page and surface as 405. The request is rejected without mutation; the status-page middleware was left outside this task.
