# Supplier portal implementation

Owner: Saja. Uses the existing Identity account, SupplierId and domain schema.
No migrations or database updates were run for this implementation.

## Implemented routes

- `/Supplier`: real supplier dashboard, invitations, offer status counts and recent notifications.
- `/Supplier/Profile`: company details; POST UpdateContact changes only phone/address and audits the change.
- `/Supplier/Invitations`: own invitations, title search/status filters, details, accept/decline. Expired is derived for unanswered invitations.
- `/Supplier/Offers`: own offers, create/edit draft, details, history, final Submit, personal UTF-8 text summary.
- `/Supplier/Documents`: official company uploads and authorized attachment downloads.
- `/Supplier/Clarifications`: own conversations with the transaction's Sourcing Officer, new conversations about invited tenders during bidding or submitted offers, replies and message attachments.
- `/Supplier/Notifications`: own inbox, mark-read, approved result notifications.

Mutations require antiforgery and server-side ownership/status validation. Draft editing/upload/submission requires
an accepted invitation, open bidding round and tender, and an unexpired deadline. Submitted offers cannot be edited
or deleted. No replacement/revision submission workflow is enabled in this supplier slice.

Amounts are rounded to the existing schema precision: prices/costs 2 decimals, quantities 3 decimals.
Submission requires positive quantities/prices, delivery days, a technical proposal, matching tender items, and
a non-expired validity date when provided. Tender requirements are free text in the current schema; the UI asks the
supplier to review required attachments, while requirement compliance remains the Officer's review responsibility.

## Files

Main pages are under `Areas/Supplier/Views`. Controllers call `SupplierProfileService`,
`SupplierInvitationService`, `SupplierOfferService` (also `SupplierOfferLifecycleService.cs`, same partial class),
`SupplierCommunicationService`, `SupplierDocumentService`, and `SupplierDashboardService`.
The supplier menu and supplier notification bell were linked to the new supplier-only inbox; other role layouts/controllers were not changed.

## Integration dependencies for teammates

- Ahmad: role seeding, real supplier registration and Identity/company linkage, email confirmation and sign-in policies remain his work. Category is still missing from the existing schema and must be agreed with the team.
- Admin: company approval/review and document verification remain Admin responsibilities. New official documents start Pending verification; existing approved company status is not automatically changed.
- Officer: invitations, clarification requests/resolution and final result dispatch are upstream responsibilities.
  To publish a result to an individual account, write its existing `Notification` with `UserId` = recipient,
  `Title` = `Tender result`, `Message` = appropriate result/next steps, and `Link` = `/Supplier/Offers/Details/{bidId}`.
  The tender must already have `Status = Awarded`, `AwardedAt`, and `AwardedBidId`.
  The supplier only derives Selected/Not selected after this notice exists; competing bid identifiers/prices/documents are never returned.
  Award approval and duplicate result dispatch prevention belong to the Officer/Manager flow; this module does not implement or bypass that flow.
- New uploads are PDF, PNG or JPEG, at most 10 MB, validated by extension/signature and stored with random names
  in `App_Data/SupplierUploads`, outside wwwroot and ignored by Git. Existing team-uploaded files must be inside private
  `App_Data` storage for this download endpoint. Downloads always verify database ownership first.
- Clarification attachment requests must be threads with the supplier user and the inviting Sourcing Officer,
  tied to the supplier's bid or invited tender. `Open/Answered/Closed` are displayed as Pending/Answered/Resolved.
- All timestamps written here are UTC. The personal summary is a text download; a branded PDF/Excel export is not implemented.

## Validation and pending functional testing

The application builds without warnings/errors. SQL translation of account, offer, invitation eligibility,
clarification participant and document ownership queries was checked without connecting to a database.
Full functional testing is deferred until Ahmad supplies real test accounts/data, as requested by Saja.

Then verify: own/other supplier access; Pending/Suspended/inactive accounts; accepted/declined/expired invitations;
duplicate responses and double submission; draft persistence and item tampering; editing/uploading after submit;
file size/signatures/private downloads; valid/forbidden clarification recipients and Closed threads; notifications
and another user's mark-read; approved results before/after Officer notification; all audit/notification records.
Also verify concurrent draft creation/edit/submission and deadline boundary behavior against SQL Server.
