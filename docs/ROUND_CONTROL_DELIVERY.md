> Historical pre-integration delivery record. For the current branch, configuration, verification and removal of the workflow test project, see [INTEGRATION_DELIVERY.md](INTEGRATION_DELIVERY.md). Statements about earlier application-database migrations describe that earlier work, not this integration.

> Current verified workflow and deployment status: [Final workflow delivery](FINAL_WORKFLOW_DELIVERY.md). Earlier status/configuration statements below are historical and superseded where that delivery describes a change.

# Round control, scheduling and re-tendering extension

Delivered: 13 November 2026 (revised the same day: Re-Tender rule corrected, approval policy configured). Continues the weighted workflow and automated report lifecycle deliveries.
Completed features were not rebuilt; the seven role layouts and unrelated modules are unchanged.

## Verification status (read first)

This work was written in a cloud workspace that **could not reach NuGet, Microsoft package feeds, container
registries or any SQL Server**. As a result:

| Check | Result |
|---|---|
| `dotnet build` of the real project (needs EF Core, Identity UI, PDFsharp from NuGet) | **Not run** (NuGet blocked: `api.nuget.org` returned HTTP 403 through the proxy) |
| EF Core migration applied to any SQL Server | **Not run** (no SQL Server; `mcr.microsoft.com`, `packages.microsoft.com`, Docker Hub blocked) |
| SQL Server integration tests | **Not run: 0 passed, 0 failed, 17 not executed** (8 SQL Server + 9 no-database tests written) |
| GitHub Actions run | **Not run.** The repository is `Tender-Management-G2/Orange-Tendering-System`, but no GitHub account is linked to the authoring session, so nothing could be pushed and no workflow could be started |
| Offline compile check with .NET SDK 10.0.115 | **Passed** for every C# file and Razor view listed below, compiled against the real ASP.NET Core 10 assemblies with EF Core, PDFsharp and xUnit replaced by minimal stand-in declarations |
| Zoom API, Ollama `gemma3:4b`, SMTP email, certificate-backed signing | **Not tested** |

The offline check proves the code is syntactically valid and type-correct against the application's own
classes. It does **not** prove that EF Core translates the queries, that the hand-written migration matches the
model, or that any test passes. Treat the feature as unverified until the build, migration check and tests
below have run.

The migration `20261113110000_RoundControlAndScheduling` is **not applied** to `OrangeTenderingSystem`.
The new code reads two new columns, so the application must not be started against that database until the
migration is applied (`dotnet ef database update --project Orange-Tendering-System`).

To verify without a developer PC, `docs/ci/sqlserver-tests.yml` is a ready GitHub Actions workflow: it builds
the solution, runs `dotnet ef migrations has-pending-model-changes`, and runs all tests against a throw-away
SQL Server container. It must be copied to `.github/workflows/` in the GitHub repository to take effect (that
folder could not be written from the authoring session, and no GitHub account was linked to it).

## What was implemented

1. **Officer controls Proceed / Re-Tender.** After **any** closed bidding round, whatever the number of offers
   (including three or more), the officer either proceeds (compliance, AI review, schedule) or re-tenders with a
   mandatory recorded reason. Re-tender is possible until the evaluation of that round is scheduled. A pending
   fewer-supplier request does not block it; the request is marked `Withdrawn`.
2. **Round limit.** Round 1 plus at most three re-tenders (`TenderWorkflowService.MaxRounds = 4`). A fifth
   round is rejected by the service.
3. **History preserved.** Re-tendering only marks the round `Failed` and adds a new round. Suppliers, accounts,
   documents, invitations and bids of earlier rounds are never modified. Bids belong to a round's invitation,
   so every round needs a new submission; earlier-round invitations no longer show a bid form.
4. **Automatic notification.** When bidding opens for round 2 or later, every still-approved supplier invited
   in an earlier round gets a new invitation and an in-app notification. Suspended or rejected suppliers are
   skipped. Unregistered companies entered by the officer (company, email, optional phone) are queued for the
   external channel and carried forward to later rounds.
5. **Fewer than three suppliers.** The officer records a justification; the Sourcing Manager assigned to the
   tender approves or rejects with a comment (`RoundDecision`). Approval relaxes only the offer count for that
   round, including round 4. At least one compliant offer from an approved supplier is still required, and
   nothing is ever awarded automatically. The existing "two offers after three failed rounds" rule is unchanged.
   The Manager's approval concerns only proceeding; it never limits the officer's re-tender authority.
6. **Weights and formulas.** Weight approval before bidding and `WeightedCalculation` were not modified.
7. **AI after compliance.** `OllamaBidAnalysisService` now requires a recorded compliance result for every
   submitted offer. `appsettings.json` points the existing integration at `http://localhost:11434` with
   `gemma3:4b`.
8. **Scheduling.** The officer either lets the system create the Zoom meeting (Server-to-Server OAuth) or
   pastes a Zoom link. One transaction saves the session, attaches the reviewed AI analysis, makes the documents
   of every eligible offer available to the four members and sends their invitations. If the session cannot be
   saved after a meeting was created, the meeting is deleted (best effort).
9. **Automatic open and close.** Scoring is accepted only inside the scheduled window (existing rule). The
   worker now also records the opening and notifies the members, and notifies the officer at automatic
   closure. Manual closure by the owning officer is unchanged. Only the four assigned members can score.
10. **Reports, signatures, watermarks, revisions, Manager review, Top Committee.** Not modified.

### Decisions made where the request left a choice

- The approver for "fewer than three suppliers" is the Sourcing Manager seated on that tender's committee.
- External invitations are registration invitations only. They do not create a supplier, an invitation or a bid.
- The confirmed policies are now set in `appsettings.json` (`TenderApprovalPolicy`, version `ORANGE-2026-11`):
  weights are approved by the `Sourcing Manager` role; Top Committee approval applies to every tender
  (`"All"`), with four assigned members, `TopQuorum` 3 and `TopApprovalsRequired` 3. A single rejection does not
  block the award (`RejectVoteBlocksAward` false), otherwise three of four could never suffice. The scope
  `"All"` and the non-blocking rejection are interpretations of "3 approvals out of 4"; change them there if the
  committee decided otherwise. Role-based weight approval means any Sourcing Manager account may approve.
- A round-4 tender with no offers has no further action; no cancel step was added.

## The two ZIP files

- `meetingZoom-…zip` contains a static page that embeds **Jitsi** (`meet.jit.si`), not Zoom. It has no Zoom API
  code, so nothing was reusable; embedding a public Jitsi room would also expose confidential meetings.
- `AIChatAssistant.zip` is a generic chat page calling Ollama `/api/chat` with a hard-coded address, the model
  `gemma3-cpu:latest` and an ASP.NET tutoring prompt. The project already has `OllamaBidAnalysisService`
  (configurable, audited, evidence-bound), so the chat controller was not copied to avoid a second Ollama
  client. Its only relevant element, the Gemma 3 model choice, is reflected in the `Ollama` settings.

## Database

Migration `20261113110000_RoundControlAndScheduling` (additive, hand-written, not applied):

| Change | Purpose |
|---|---|
| Table `RoundDecision` | Re-tender records and fewer-supplier requests with requester, justification, offer count, decider, comment, timestamps, rowversion |
| Table `ExternalSupplierInvitation` | Unregistered contacts per round with channel, status, attempts, last error |
| `EvaluationSession.ZoomMeetingId` (nullable) | Meeting created through the Zoom API |
| `EvaluationSession.OpenedAt` (nullable) | When the system announced the opening |

`ApplicationDbContextModelSnapshot.cs` was edited by hand to match. If
`dotnet ef migrations has-pending-model-changes` reports differences, regenerate the snapshot with
`dotnet ef migrations add` rather than editing further.

## Configuration

```json
"Zoom": { "AccountId": null, "ClientId": null, "ClientSecret": null, "HostUserId": null },
"ExternalInvitations": { "Enabled": false, "SmtpHost": null, "SmtpPort": 587, "UseSsl": true,
  "UserName": null, "Password": null, "FromAddress": null, "PublicBaseUrl": null, "MaxAttempts": 3 }
```

Keep `Zoom:ClientSecret` and `ExternalInvitations:Password` in user secrets or deployment configuration.
Without Zoom credentials the officer pastes a Zoom link. Without an external channel, contacts are stored with
status `ChannelNotConfigured` and nothing is reported as sent.

## Authorization, audit, notifications

- Officer actions (`Retender`, `RequestProceed`, `InviteExternal`, `Setup`) are in the SourcingOfficer area and
  re-checked in the service against tender ownership. `DecideProceed` is in the SourcingManager area and requires
  the manager seat on that tender; the requesting officer cannot decide.
- New audit actions: `RetenderEvidence`, `ProceedRequestEvidence`, `ProceedDecisionEvidence`,
  `ExternalInvitationEvidence`, `RoundSuppliersNotified`, `EvaluationScheduleEvidence`, `EvaluationAutoOpened`,
  `ExternalInvitationSent`, `ExternalInvitationFailed`, `ExternalInvitationNotSent`.
- New notifications: re-tender (manager), new round (supplier accounts), proceed request and decision,
  evaluation invitation with meeting details, scoring opened, closed at deadline.

## Tests (written, not executed)

`Orange-Tendering-System.Tests` (xUnit). Integration tests create `OrangeTenderingSystem_Test_<guid>` through
the real migrations and drop it afterwards; any other database name is refused. Server:
`ORANGE_TEST_SQLSERVER` (default local instance, Windows authentication). No SQLite or EF InMemory.
Test rows exist only in that database. Zoom and the external channel are replaced by in-process fakes, and the
AI analysis row is a fixture, so these tests say nothing about the real Zoom, SMTP or Ollama services.

- `RoundControlTests`: re-tender history and round-5 block; re-tender with three offers, withdrawal of a pending
  request and no re-tender after scheduling; suspended supplier and external carry-forward; fewer-supplier
  approval; approval cannot bypass compliance after the final round.
- `SchedulingAndLifecycleTests`: Zoom creation after pre-check; scheduled open/close and assigned-member scoring;
  full workflow with manual close, signed PDF report, Manager return and versioned revision, Manager approval and
  three-of-four Top approval before award.
- `CalculationAndGuardTests`: unchanged formulas, round limit, database-name guard, disabled-by-default channels.

## Files

New: `Models/Entities/RoundControl.cs`; `Data/Migrations/20261113110000_RoundControlAndScheduling.cs` and
`.Designer.cs`; `Services/TenderWorkflowService.Rounds.cs`; `Services/ZoomMeetingService.cs`;
`Services/ExternalInvitationDispatcher.cs`; `Views/Shared/_RoundControls.cshtml`;
`Orange-Tendering-System.Tests/` (project, `SqlServerFixture.cs`, `Scenario.cs`, three test files);
`docs/ci/sqlserver-tests.yml`; this document.

Changed: `Orange-Tendering-System.slnx`; `Program.cs`; `appsettings.json`; `Models/Entities/WeightedWorkflow.cs`;
`Data/ApplicationDbContext.cs`; `Data/Migrations/ApplicationDbContextModelSnapshot.cs`;
`Services/TenderWorkflowService.cs` (Retender moved to the Rounds file, OpenBidding, Setup, Details, deadline
notification); `Services/OllamaBidAnalysisService.cs`; `Services/EvaluationDeadlineWorker.cs`;
`Services/README.md`; `ViewModels/WeightedWorkflowModels.cs`; SourcingOfficer and SourcingManager
`WorkflowController.cs`; the six identical role `Details.cshtml` pages (five `Workflow`, one `WeightApprovals`).

## Remaining blockers

1. Real build, migration check and all 17 tests have not run. Running them in GitHub Actions needs a linked
   GitHub account with push access to `Tender-Management-G2/Orange-Tendering-System`.
2. Migration not applied to `OrangeTenderingSystem`.
3. Zoom credentials, SMTP settings and a running Ollama with `gemma3:4b` were not available; none is verified.
4. Certificate-backed signing remains unverified, as in the previous delivery.
5. The migration and model snapshot remain hand-written and unvalidated by EF Core tooling.
