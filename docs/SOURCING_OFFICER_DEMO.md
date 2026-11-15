# Sourcing Officer demo accounts

These accounts and records are inserted on application startup only when the environment is **Development** and `DemoData:Enabled` is `true`. Seeding is idempotent; it checks for the `DEMO - Information Technology` department before creating the sample set.

**Password for every demo account:** `OrangeDemo2026!`

| Role | Login |
| --- | --- |
| Sourcing Officer | `officer@orange-demo.local` |
| Requesting Department | `requester@orange-demo.local` |
| Sourcing Manager | `manager@orange-demo.local` |
| Evaluation Committee | `committee1@orange-demo.local` through `committee4@orange-demo.local` |
| Suppliers | `supplier1@orange-demo.local` through `supplier4@orange-demo.local` |

## Scenarios

- **Requests:** one accepted request ready for a tender, one submitted for review, one returned, and one rejected.
- **Invitations:** accepted, pending, and declined supplier responses on the active demo tender.
- **Offers:** submitted compliant and non-compliant offers are available under an evaluation tender.
- **Supplier categories:** the demo suppliers are spread across IT & Electronics, Office Supplies, Furniture & Fixtures, and Professional Services. The directory also lists other standard categories with their approved supplier counts.
- **Committee reports:** separate tenders show a submitted report, a returned report with a clarification note, a rejected report, and an accepted report.
- **Chat:** one open supplier question is preloaded for the non-compliant offer. Reply as the officer, then sign in as supplier 3 to see the response in the same conversation.

The demo seed does not run in Production. Set `DemoData:Enabled` to `false` in Development to stop creating demo data on startup.
