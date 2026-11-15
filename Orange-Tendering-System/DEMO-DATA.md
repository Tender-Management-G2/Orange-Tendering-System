# Tender cycle demo accounts

## Team login accounts (always seeded in Development)

`Services/TeamAccountSeeder.cs` creates these on every Development start (switch `TeamAccounts:Enabled`, default `true`). Password for all: `OrangeDemo2026!`. Existing accounts keep their password.

| Role | Email |
| --- | --- |
| Sourcing Officer | `sourcing.officer@orange-demo.local` |
| Sourcing Manager | `sourcing.manager@orange-demo.local` |
| Requesting Department (Team Demo Department) | `department@orange-demo.local` |
| Top Committee — CEO | `top.ceo@orange-demo.local` |
| Top Committee — CFO | `top.cfo@orange-demo.local` |
| Top Committee — CTO | `top.cto@orange-demo.local` |
| Top Committee — COO | `top.coo@orange-demo.local` |

Only one Sourcing Officer may be active. If another officer is already active in your database, the team officer is created inactive and a warning is logged; switch officers in Admin > Users.

## Optional full demo dataset

The optional `DemoDataSeeder` creates ten approved supplier companies with linked Supplier accounts, four Evaluation Committee accounts, and ten Top Committee accounts. It preserves the four original supplier scenarios and adds accounts 5–10 without pre-inviting them. The Sourcing Officer can choose them from the approved suppliers list when opening a tender round. `committee1@orange-demo.local` belongs to the demo IT department and can fill the Department Representative seat; the other evaluation users can fill the Financial and Technical seats. Top Committee accounts appear as candidates for the four seats on each tender; `top1@orange-demo.local` is labeled as the CEO demo account.

This runs only in Development when `DemoData:Enabled` is true. For a one-time seed, set `DemoData__Enabled=true` in the app process environment and start the app. The seeder is repeatable: it looks up the synthetic suppliers by registration number and users by email, and does not reset passwords, approval decisions, invitations, bids, or committee assignments. Keep this option disabled outside a demo environment.

| Group | Login emails | Initial password |
| --- | --- | --- |
| Suppliers | `supplier1@orange-demo.local` through `supplier10@orange-demo.local` | `OrangeDemo2026!` |
| Evaluation Committee | `committee1@orange-demo.local` through `committee4@orange-demo.local` | `OrangeDemo2026!` |
| Top Committee | `top1@orange-demo.local` through `top10@orange-demo.local` | `OrangeDemo2026!` |
| Sourcing Officer | `officer@orange-demo.local` | `OrangeDemo2026!` |
| Sourcing Manager | `manager@orange-demo.local` | `OrangeDemo2026!` |

The suppliers span the ten named procurement categories. Each supplier must enroll a face or passkey through the normal supplier setup before entering the protected supplier workspace. A password by itself opens setup, not the supplier dashboard. Top Committee members sign in with their demo password after an Admin assigns four distinct accounts to the tender; creating the accounts alone does not authorize voting on a particular tender.

To walk the cycle: sign in as Sourcing Officer, open an active tender round, choose a category and invite the desired suppliers. Have those suppliers complete biometric enrollment, sign in, accept invitations and submit offers. Continue through evaluation and Sourcing Manager review, then assign four Top Committee members as Admin and have them review the forwarded tender.
