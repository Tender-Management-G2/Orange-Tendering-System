# Final Decision relay and AI Chat Assistant — 13 November 2026

Branch: `codex/final-decision-ai-chat-20261113` (based on `tariq`; not merged into `main`). Screenshots: `screenshots/`.

## 1. Final Decision & Supplier Notifications
Chain enforced by data (`Services/TenderWorkflowService.FinalDecision.cs`):
1. **Top Committee**: when approvals reach the configured threshold (policy `TopApprovalsRequired`, currently 3 of 4) the recording of the vote creates one `FinalDecisionNotice` and notifies the **assigned Sourcing Manager** (evaluation-committee Manager seat). Tenders that do not need Top review get their notice when the Manager approves.
2. **Manager** acknowledges and forwards (`SourcingManager/FinalDecisions`, popup) → the Officer is notified.
3. **Officer** (single active Sourcing Officer) — `Award` now refuses until the Manager forwarded the decision, and **no longer notifies suppliers**. Only `SourcingOfficer/FinalDecisions` → *Notify Suppliers* (confirm popup) announces winning/unsuccessful results, once.
- SQL: `FinalDecisionNotices`, `SupplierResultDeliveries` (per supplier user: outcome, in-app status, e-mail status, attempts, last error; unique per notice+user), existing `Notifications` (unread counts, SignalR bell) and `AuditLogs`. Migration `AddFinalDecisionRelay` (additive).
- Delivery: in-app always; SMTP only when `Smtp:*` is configured (`NotConfigured` otherwise); failed e-mails can be retried by the Officer, capped at 3 attempts. Messages mention only the recipient's own company; supplier pages remain isolated.

## 2. AI Chat Assistant
- The supplied `AIChatAssistant.zip` was a stand-alone .NET 8 sample (hard-coded `localhost:11434`, model `gemma3-cpu`, no auth, no storage). Its idea (Ollama `/api/chat` with a scoped system prompt) was reused; its code was not copied.
- One shared `OllamaChatClient` (Ollama settings: `http://localhost:11434`, `gemma3:4b`) now serves both AI Bid Analysis and the chat — no second client.
- Floating popup on every role layout (`_AiChatWidget`, `ai-chat.js`): loading dots, error + Retry, history, new chat, delete. SQL: `AiChatConversations`, `AiChatMessages` (migration `AddAiChatAssistant`), strictly per user; 2,000-character limit, 8 messages/minute. The assistant receives no tender/offer/supplier data. AI Bid Analysis (`AiAnalysis`) and Clarifications are untouched.

## Verification (isolated `OrangeAuthCheck_UiE2E_*` SQL Server database, real Kestrel app, live Ollama)
| Area | Result |
| --- | --- |
| Release build `dotnet build -c Release --no-incremental` | PASS, 0 warnings / 0 errors |
| Existing auth harness (`Tests/AuthSmoke`) | PASS 211 (harness updated for the new required supplier Category field from `main`) |
| Delivery layer (`--final-decision-checks`, scripted SMTP, real SQL): authority, concurrent double-click, one row per supplier user, Sent/Failed/retry/cap, no competitor data in e-mails, audit | PASS 17/17 |
| Playwright `fd_chat_suite.py`: relay order, Officer-only notify, no early supplier notice, live bells, duplicate refusal, isolation, chat answers/persistence/history/isolation/validation/rate limit/Ollama-down | PASS 43/43 |
| Earlier UI suite (76) and 69-page sweep after adding the widget | PASS |

## Not covered / BLOCKED
- **BLOCKED — live SMTP**: no SMTP provider is configured; Sent/Failed/retry were verified with a scripted sender, and `NotConfigured` with the real server.
- **Not exercised end to end in a browser**: casting the four signed Top Committee votes (requires the multi-phase timed lifecycle in `docs/test-evidence`). The 3-of-4 trigger is called through the real service code path in the fixture (`RecordFinalDecision`); the votes/signatures themselves are unchanged existing workflow.
- Demo seeder (`DemoData:Enabled`) creates a second Sourcing Officer, which makes the single-Officer guard fail closed; tests run with it disabled.

- After merging `main`, `appsettings.Development.json` configures SMTP at 127.0.0.1:1025 (no listener in the test environment): the real server then records e-mail status `Failed` (attempt 1) with the Retry button, which is the correct behavior; `NotConfigured` applies when no `Smtp` section exists.
