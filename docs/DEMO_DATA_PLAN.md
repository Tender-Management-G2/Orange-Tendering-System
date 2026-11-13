# Demo Data Plan

A **plan** for realistic development and demo data. Nothing here is inserted into SQL Server, and there are no
seeders, scripts or passwords. When the features exist, the team creates this data **through the application**
(or a reviewed, opt-in development seeder that Ahmad owns), in the order of §10. Business rules:
[`TenderingOrangeSystem.md`](../TenderingOrangeSystem.md).

**Rules for all demo data**
- Fictional people and companies only; email addresses use the reserved `example.test` domain.
- **No passwords in Git.** Each developer sets demo passwords locally (user secrets or at the sign-up screen).
- Money in **JOD**; timestamps stored in **UTC**.
- **No scoring numbers are planned:** the score scale, weights, aggregation and tie rules are not approved yet.
  The committee enters criteria and scores during the demo using whatever method is approved by then.
- Never present demo records as real statistics.

## 1. Roles (seeded first)

`Admin`, `Requesting Department`, `Sourcing Officer`, `Supplier`, `Evaluation Committee`, `Sourcing Manager`,
`Top Committee` — exactly as in `Models/SystemRoles.cs`.

## 2. Departments

| Name | Description |
|---|---|
| IT | End-user computing and business applications |
| Network Operations | Core and access network |
| Finance | Budgeting, accounts payable |
| Facilities | Buildings and maintenance |
| HR | People services (no requests in the scenario; shows an empty department) |

## 3. Internal accounts

| Person (fictional) | Email | Department | Global roles | Committee seats in the scenario |
|---|---|---|---|---|
| Rana Haddad | admin@example.test | — | Admin | none (Admin never votes) |
| Omar Khalil | omar.khalil@example.test | IT | Requesting Department | Evaluation — DepartmentRepresentative (Tender 1) |
| Dina Saleh | dina.saleh@example.test | Network Operations | Requesting Department | Evaluation — DepartmentRepresentative (Tender 2) |
| Yousef Nasser | yousef.nasser@example.test | — | Sourcing Officer | — |
| Layla Mansour | layla.mansour@example.test | — | Sourcing Manager, Evaluation Committee | Evaluation — SourcingManager (both tenders) |
| Karim Azar | karim.azar@example.test | Finance | Evaluation Committee | Evaluation — FinancialRepresentative |
| Huda Jaber | huda.jaber@example.test | Network Operations | Evaluation Committee | Evaluation — TechnicalRepresentative |
| Samer Odeh | samer.odeh@example.test | — | Top Committee | Top (both tenders) |
| Nour Abbas | nour.abbas@example.test | — | Top Committee | Top |
| Faris Hamdan | faris.hamdan@example.test | — | Top Committee | Top |
| Maya Qasem | maya.qasem@example.test | — | Top Committee | Top |

Layla shows the rule that the **Sourcing Manager is one of the four evaluators** and still performs the separate
manager review. No person appears on both committees of the same tender (unique `CommitteeMembers(TenderId, UserId)`).

## 4. Supplier companies

| Company | Reg. no. | Tax no. | Status | Login account | Legal documents |
|---|---|---|---|---|---|
| Levant Tech Supplies | CR-100231 | TX-55120 | Approved | sales@levanttech.example.test | Commercial registration (verified), tax certificate (verified) |
| Petra Computing Co. | CR-100877 | TX-55871 | Approved | bids@petracomputing.example.test | Commercial registration (verified), tax certificate (verified) |
| Aqaba Digital Systems | CR-101452 | TX-56204 | Approved | tenders@aqabadigital.example.test | Commercial registration (verified), ISO certificate **expiring in 20 days** |
| Amman NetWorks | CR-102019 | TX-56990 | Approved | office@ammannetworks.example.test | Commercial registration (verified) |
| Desert Line Trading | CR-102555 | TX-57311 | Approved | info@desertline.example.test | Commercial registration (verified), tax certificate (**rejected**, re-uploaded and verified) |
| Cedar IT Services | CR-103004 | — | **Pending** | hello@cedarit.example.test | Commercial registration (pending verification) |
| Orbit Electronics | CR-099870 | TX-54002 | **Suspended** (review note: expired license) | sales@orbit.example.test | License (expired) |

Pending and suspended companies must never appear in supplier selection.

## 5. Purchase requests

| Ref | Department / creator | Title | Items | Path |
|---|---|---|---|---|
| PR-1 | IT / Omar | Laptops for the new analytics team | 10 × "Business laptop — 16 GB RAM, 512 GB SSD, 14-inch" (unit: pcs); 10 × "USB-C docking station" (pcs) | Submitted → **Returned** ("specify warranty and keyboard layout") → edited to **15 laptops**, specs updated → resubmitted → **Accepted**. Two `PurchaseRequestReviews` rows; submission 1 snapshot still shows 10 laptops. |
| PR-2 | Network Operations / Dina | Access switches for branch offices | 8 × "48-port PoE+ access switch, 4×10G uplinks" (pcs) | Submitted → Accepted |
| PR-3 | Facilities / (Facilities employee optional) | Office chairs | 40 × chair | Submitted → **Rejected** ("covered by the annual framework contract") |
| PR-4 | IT / Omar | Monitor upgrade | 20 × 27-inch monitor | **Draft** only (never submitted) |

## 6. Tenders, rounds, invitations and offers

### Tender 1 — from PR-1 (laptops) — happy path with one failed round

Tender items (copied from PR-1 as accepted): 15 × laptop (spec incl. 3-year warranty, English/Arabic keyboard),
10 × docking station. Requirements: valid commercial registration, tax certificate, 3-year onsite warranty.

| Round | Invited | Responses | Submitted offers | Outcome |
|---|---|---|---|---|
| 1 | Levant Tech, Petra Computing, Aqaba Digital, Desert Line | Levant accepts, Petra accepts, Aqaba declines, Desert Line ignores | Levant (rev 1), Petra (rev 1) = **2 distinct** | Closed → **Failed** (fewer than 3) |
| 2 | Levant Tech, Petra Computing, Desert Line, Amman NetWorks | all accept | Levant (rev 1), Petra (rev 1 → **rev 2** after a clarification; rev 1 Superseded), Desert Line (rev 1) = **3 distinct** | Closed → **SentToEvaluation** |

Round-2 offer examples (rev that counts):

| Supplier | Laptop unit price | Dock unit price | Shipping | Installation | Maintenance | Tax | Delivery | Valid until | Compliance |
|---|---|---|---|---|---|---|---|---|---|
| Levant Tech | 735.00 | 95.00 | 120.00 | 0.00 | 450.00 | 1,780.00 | 21 days | +60 days | **Compliant** |
| Petra Computing (rev 2) | 698.00 | 110.00 | 0.00 | 150.00 | 600.00 | 1,720.00 | 30 days | +45 days | **Compliant** (was PendingClarification on rev 1: missing warranty letter) |
| Desert Line | 655.00 | 88.00 | 90.00 | 0.00 | 0.00 | 1,610.00 | 14 days | +30 days | **NonCompliant** — "warranty 1 year, 3 years required"; missing: "3-year warranty statement" |

Desert Line **counts** toward the 3-offer minimum but is **not eligible** to be recommended or awarded.

### Tender 2 — from PR-2 (switches) — the two-offer exception

| Round | Submitted distinct offers | Outcome |
|---|---|---|
| 1 | 1 (Amman NetWorks) | Failed |
| 2 | 2 (Amman NetWorks, Levant Tech) | Failed |
| 3 | 0 | Failed |
| 4 | 2 (Amman NetWorks, Aqaba Digital) | Officer forwards under the **two-offer exception** |

This assumes "three failed rounds in total". If the team decides the initial round does not count, add one more
failed round before the exception (open decision, spec §21). Earlier rounds' offers are never carried forward.

## 7. Committees, evaluation and reports (Tender 1)

**Evaluation Committee (Tender 1):** Layla (SourcingManager), Omar (DepartmentRepresentative), Karim
(FinancialRepresentative), Huda (TechnicalRepresentative).
**Top Committee (Tender 1):** Samer, Nour, Faris, Maya.

| Step | Records |
|---|---|
| Officer comparison | `TenderReport` OfficerComparison v1 (Yousef) → Submitted; becomes the input of Evaluation round 1 |
| Evaluation round 1 | Criteria entered by the committee (e.g. technical: "Specification match", "Warranty & support"; financial: "Total cost", "Delivery time") — **weights and scores entered with the approved method; none planned here**. Votes: Layla, Omar, Karim **Approve**, Huda **Reject** ("docking-station compatibility not verified") → round **Rejected** |
| Clarification | Huda ↔ Yousef: compatibility question; Yousef ↔ Petra: confirmation letter (document attached to the message) |
| Evaluation round 2 | Same members, criteria recorded again for this round, scores entered → **4/4 Approve** → `FullEvaluation` v1 with RecommendedBid = (whichever eligible offer the approved method ranks first) and `RankingSnapshotJson` |
| Officer → Manager | Yousef submits FullEvaluation v1 → Layla (as Manager) **Returns** it ("add delivery-risk explanation") → Yousef submits **v2** (`PreviousReportId` = v1) → Layla **Accepts** |
| Executive summary | Layla writes `ExecutiveSummary` v1 → Top round 1: Samer, Nour, Faris Approve; Maya **Reject** ("summary omits warranty terms") → Layla writes v2 → Top round 2: **4/4 Approve** |
| Award | `Tender.AwardedBidId` = the recommended eligible offer, `AwardedAt` set, Tender Status = Awarded; recommendation and award remain separate fields |

Tender 2 can stop at the Officer comparison report to keep the demo short.

## 8. Clarifications and notifications

| Thread (subject) | From → To | Context | Messages | Status |
|---|---|---|---|---|
| "Keyboard layout and warranty" | Yousef → Omar | PR-1 | 2 (question, answer) | Closed |
| "Warranty letter missing" | Yousef → Petra account | Bid (Petra rev 1) | 3 (+ PDF on the reply) | Closed |
| "Docking-station compatibility" | Huda → Yousef | Tender 1 | 2 | Answered |
| "Delivery-risk wording" | Layla → Karim (committee ↔ manager) | Tender 1 | 2 | Closed |
| "Warranty terms in summary" | Maya → Layla (top ↔ manager) | Tender 1 | 2 | Closed |

Notifications to generate along the way: request submitted/returned/accepted; invitation received; offer
received; clarification message; evaluation assigned; round approved/rejected; report submitted/returned;
summary forwarded; award decided.

## 9. Audit events to expect

Every step above writes an `AuditLogs` row (actor, action, entity, id, safe old/new values, UTC), for example:
`Submit PurchaseRequests PR-1`, `Return PurchaseRequests PR-1`, `Create TenderRounds`, `Close TenderRounds
(Failed)`, `Submit Bids`, `Update Bids (ComplianceStatus NotChecked → NonCompliant)`, `Vote CommitteeVotes`,
`Return TenderReports`, `Approve Tenders (Award)`, `Approve Suppliers`, `AssignRole AspNetUserRoles`.
Never log passwords, tokens or document contents.

## 10. Creation order

1. Roles → initial Admin (secret supplied at run time) → departments.
2. Internal employees and their roles (Admin UI).
3. Supplier registrations → legal documents → Admin verification → Approved / Pending / Suspended states.
4. Purchase requests: drafts → submissions → Officer decisions (return, resubmit, accept, reject).
5. Tenders and items from accepted requests → committee assignment (both committees, responsibilities).
6. Round 1 → supplier selection → invitations → responses → offers (revisions) → close → failed → round 2 …
7. Compliance reviews → clarifications → Officer comparison report → forward to evaluation.
8. Evaluation rounds: criteria → scores → votes (one rejected round, one 4/4 round) → full evaluation report.
9. Officer → Manager (return, new version, accept) → executive summary → Top rounds (one rejected, one 4/4) → award.
10. Check notifications, clarifications, analytics pages and audit logs show every step.

Each step depends on the previous one's records (foreign keys are `Restrict`, so nothing can be skipped or deleted
to "reset" the demo — use a fresh local database instead).
