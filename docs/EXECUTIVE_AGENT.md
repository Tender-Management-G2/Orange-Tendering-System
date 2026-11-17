# Orange AI Executive Agent

This is an additive feature on branch `tariq22222`. The existing Gemma chat settings, client and service are unchanged. The existing seven role layouts include the new partial through `_AiChatWidget`; the guide itself remains separate. No commit or merge is authorized by this task.

## Execution boundary

`ExecutiveAgentClient` calls local Ollama `/api/chat` with `qwen3.5:9b`, structured tool schemas, `think:false`, temperature zero and bounded context/steps. It cannot pull models and rejects a non-loopback endpoint or a different model. `ExecutiveAgentService` dispatches only `message.tool_calls`. JSON, XML or tool-like text in `message.content` is never interpreted as an invocation. Model prose is advisory; SQL tool results and execution receipts are authoritative.

Available tools:

- `search_records`: existing scoped search, using the authenticated user's actual Identity roles.
- `tender_status`: limited live status; checks officer, assignment or department access. Suppliers use only their existing scoped invitation/offer search.
- `open_report`: existing report review authorization and integrity checks; returns an authenticated PDF link. A link being prepared does not prove the user opened the PDF.
- `draft_tender`: Sourcing Officer only, read-only. Picks the accepted purchase request without a tender (or lists them when several exist) and returns a link that opens the existing New Tender form (`_TenderCreateDialog`) prefilled with the request and the requested title (`?request=ID&title=`). It creates nothing; the officer completes the remaining fields and submits the form, so `TenderWorkflowService.Create` keeps every rule.
- `prepare_action`: persists a pending action, never executes it. Supported operations are weight request, weight approval, evaluation report signing, manager report approval, Top voting, report forwarding, final-decision forwarding and supplier notifications.

Cancellation uses the existing Top `Reject` decision and subsequent manager/officer relay. There is no arbitrary cancellation endpoint, generated SQL, general HTTP tool, browser automation tool, impersonation or permission escalation. Other procurement tasks remain in the existing workflows.

## Human review and identity

The model cannot confirm an action or obtain identity proofs. The review card shows the exact operation, tender, decision, comment, expiry, workflow snapshot hash and current report hash. Actions expire after ten minutes. Report signing also requires the existing protected-PDF view record and exact document hash.

Approvals, signatures, Top voting (including the cancellation decision) and supplier notifications require the existing Identity `PasskeyService` WebAuthn verifier with user verification required. The fresh challenge is stored against the immutable action manifest; a proof for another action, user, origin or expired challenge is rejected. The action stores its manifest and public WebAuthn assertion as authorization evidence, not biometric samples or private keys. An account's additional MFA policy is never silently bypassed: accounts with `TwoFactorEnabled` must continue through the existing full identity flow, and agent execution is blocked until its integration is defined.

Only weight-request and forwarding actions can alternatively use existing Identity password reauthentication with lockout accounting. Passwords are not persisted or supplied to Ollama. Passkey failures are throttled.

A WebAuthn **action authorization receipt is not a certificate-backed PDF signature**. Actual report signatures continue through `ReportDocumentService` and `ReportCryptography`, including the existing configured certificate policy. If trusted personal certificates are required but unavailable, signing fails. The agent does not downgrade that policy. If the existing policy permits electronic acknowledgment, the report records that method honestly while the agent action has separate WebAuthn authorization evidence.

## Transactions, idempotency and notifications

The SQL migration adds `ExecutiveAgentRuns` and `ExecutiveAgentActions`; existing `AuditLogs` store progress and outcomes. Runs have unique request IDs, and pending actions have unique semantic deduplication hashes. Expired pending actions allow a new review. Confirmation locks the action and tender inside a serializable SQL transaction, rechecks account/role/assignment and current workflow snapshot, calls the existing workflow service, and commits the business change, receipt and success marker together. Repeated confirmations return the recorded result. The workflow `Change` helper joins an existing transaction when present; standalone workflows retain ownership of their original transaction.

Supplier notifications call the existing authorized workflow. SQL delivery records commit before the existing mail sender is invoked. The agent does not accept arbitrary recipient addresses or bodies. Email outcomes remain per-recipient statuses in the existing UI and audit. SQL success does not prove email delivery. A crash after SQL commit but before email attempts leaves pending rows; review those through the existing workflow. SMTP cannot provide an exactly-once delivery guarantee across a crash after transport acceptance; no such guarantee is claimed.

## Voice

The top microphone defaults to direct voice commands without opening a modal. It uses the top-bar Arabic/English language selection, records locally, and stops after approximately 1.4 seconds of silence after speech (or a second microphone click; 30-second limit). The recognized command first goes to the permission-scoped navigation resolver. A unique permitted GET page opens automatically; ambiguous destinations require a choice. Unknown commands go to Qwen's structured tools. Prepared approvals and all other mutations still remain pending for explicit review and identity verification. **AI** remains an optional detailed/manual command dialog, and the previous voice-search mode remains selectable.

`ExecutiveNavigationService` derives its catalog from `WorkspaceNavigation`, actual registered GET action descriptors, generated routes and current database roles/authorization policies. Arabic/English aliases normalize common navigation phrases. The model selects a `navigate_page` ID from that catalog; it cannot provide an arbitrary destination URL. Navigation is audited in SQL Server. This is live site grounding, not a claim of model fine-tuning or perfect recognition of every phrase.

The first recognition option is browser Web Speech with `processLocally:true` and an already installed language pack reported as available. The feature never calls language-pack installation or falls back to cloud recognition. If unavailable, it can use an explicitly configured local whisper.cpp runtime and multilingual model:

```json
"ExecutiveSpeech": {
  "WhisperExecutable": "<absolute path to installed whisper-cli.exe>",
  "ModelPath": "<absolute path to an installed multilingual ggml model>",
  "FfmpegExecutable": "<absolute path to installed ffmpeg.exe>"
}
```

These are administrator configuration paths, never request arguments. On a developer machine, put them in User Secrets (`dotnet user-secrets set "ExecutiveSpeech:ModelPath" "<path>"`), not in a committed appsettings file. The page checks the configured server recognizer first, then an installed on-device browser language pack, and shows the actual reason when neither is available. Verified setup: whisper.cpp `whisper-bin-x64.zip` (release b5454) with `ggml-large-v3-turbo-q5_0.bin`; a short English recording transcribes locally in about 18 seconds on CPU. Audio is bounded to 30 seconds / 5 MB, converted to 16 kHz mono locally, and removed with its temporary transcript after recognition. Processes use fixed argument lists without a shell. An unconfigured runtime or unavailable language produces a visible error and text fallback. No speech models have been downloaded by this implementation. Actual Arabic and English recognition accuracy depends on the installed runtime/model and remains a required browser test.

## Verification and outstanding blockers

Run from the authoritative repository in a normal PowerShell session:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Test-ExecutiveAgent.ps1 -Browser
```

The script checks root and branch, builds `AgentVerify`, and runs real Qwen/Identity/workflow integration checks against a new **local** `OrangeAuthCheck_Executive_<UTC>` SQL Server database. It does not migrate the team/production database or send supplier emails. The isolated software WebAuthn authenticator signs real cryptographic assertions; it does not verify hardware biometrics. The checks include English/Arabic model dispatch, actual weight-request execution, actual weight approval, bad credentials/forged signature rejection, duplicate requests/confirmations, stale state, permission denial and SQL audit evidence. These are executable checks, not a claim that they have passed.

The optional browser host binds only the repository's ports 5254 and 7182 and refuses an occupied port. Stop normal Visual Studio debugging first. Local isolated test credentials and connection details are in ignored `App_Data/ExecutiveAgentTest/session.json`; never commit or share this file. The console log is in ignored `.build/executive-agent/sql-checks.log`.

Required browser test after the host starts: sign in with the isolated fixture account; select Executive Agent microphone mode; verify local speech availability separately for Arabic and English; speak a command to locate the isolated tender, read its status and prepare an allowed action; review the actual transcript; run; review the pending card; confirm using existing identity; inspect the real SQL change and audit. Repeat with a denied role, bad proof and duplicate confirmation. A typed command, injected transcript, fixture tool callback or software authenticator must never be reported as a completed human voice/hardware identity test.

Verified during implementation: application and integration test project compile; JavaScript syntax and whitespace checks pass; EF reports no pending model differences. The build currently reports the pre-existing Top Committee CS9107 warning and two test-only WebHostBuilder/TestServer deprecation warnings. The initial real Ollama fixture dispatch tests passed English and Arabic in the earlier turn. The sandbox cannot open the local SQL named pipe, SQL Server TCP is disabled, and the permissions tool cannot represent that pipe path. A normal-session run migrated a new isolated SQL database and passed checks 1–10, then failed the original missing-CSRF status assertion.

The CSRF failure was reproduced on the actual controller's security filters: missing and invalid tokens were rejected by MVC, but the empty 400 response was re-executed as POST `/status/400`, whose GET-only route changed the response to empty 405. The controller-scoped `AgentAntiforgeryFailureAttribute` now preserves the rejection as JSON 400; `Authorize` and `AutoValidateAntiforgeryToken` remain enabled. Twelve filter-level checks pass (missing, invalid and valid tokens on all four POST endpoints). The full SQL harness now includes the same matrix with real authenticated accounts and actual business validation. **That expanded suite has not completed; neither the isolated browser host nor a complete real voice-to-action workflow has been verified ready.** The migration has not been applied to the existing application database. Full seven-workflow regression, report-forward/sign/vote/cancellation paths and external email delivery remain unverified. No commit or merge has been performed.

Reference contracts: [Ollama tool calling](https://github.com/ollama/ollama/blob/main/docs/capabilities/tool-calling.mdx), [on-device SpeechRecognition](https://developer.mozilla.org/en-US/docs/Web/API/SpeechRecognition/processLocally), [SQL Server connection protocols](https://learn.microsoft.com/en-us/sql/connect/ado-net/connection-string-syntax?view=sql-server-ver17).

## Verified model-parser regression and repair

## Site knowledge expansion (verification in progress)

`site_help(topic)` reads reviewed workflow guidance for the user's **current SQL Identity roles**, plus explanations of every menu category in the actual authorized page catalog. It has no business mutation. For procedural questions the server automatically loads the reviewed guide before inference and audits AgentKnowledgeLoaded with a context hash. This is explicitly NOT a model tool invocation. The optional site_help tool can reread the guide through a genuine native call. The model must distinguish explanation/navigation/preparation/execution and hand unsupported operations to existing forms. The existing Gemma chat is unchanged. The Executive Agent has its own reviewed guide aligned to current services: Officer weight approval and the connected Top rejection/decision relay take precedence over older help prose.

Arabic/Jordanian and English navigation aliases cover the seven role menus. Ambiguous menu names require a choice; compound commands, specific record commands and mutations must not be reduced to a generic page match. `ExecutiveKnowledgeChecks` checks every declared menu against registered GET actions and reviewed procedural coverage, aliases and unsafe compound phrases. Its optional `--live-model` mode uses the actual installed Qwen and production tool validator for Arabic and English help requests across all seven roles, checks that the server guide was loaded first, checks the answer language and role, and rejects any navigation or mutation during explanation. Optional native guide rereads are logged separately; generated tool-like text never invokes a tool. This does not replace SQL authorization/execution tests or human microphone testing.

This is site grounding, **not fine-tuning**. It does not promise arbitrary-command understanding, zero errors or automatic execution of every site operation. Connected writes remain exactly the `prepare_action` allowlist, separate explicit confirmation, Identity verification and existing workflow checks. Other writes remain in their original forms. New menus need reviewed guide/alias coverage and cause the coverage check to fail when omitted.

Local speech runtime update: with the user's installation authorization, the configured Whisper path now contains the official b5454 CUDA 11.8 runtime and matching NVIDIA cuBLAS dependencies. The previous CPU runtime is retained locally for rollback. The same multilingual `large-v3-turbo-q5_0` model remains in use. A four-second Arabic clip measured 17.97 seconds on default CPU versus 2.32 seconds through the configured deployed CUDA CLI, with the same recognized text. Logs confirmed `using CUDA0 backend`; Qwen remained loaded. These are process timings, not a measured end-to-end browser latency. Downloads were an explicit setup operation; the app still never downloads speech or Ollama models automatically.

## Prior model-parser verification

The expanded suite stopped after check 55, before the English `AGENT-EN-TEST` command could dispatch its first tool. Ollama 0.40.16 was available, with the installed `qwen3.5:9b` loaded at 8192 context. Its log and direct HTTP reproduction reported HTTP 500 in approximately 2.5–3 seconds: `expected element type <function> but have <parameter>`. The response was valid JSON containing an upstream parser error, not malformed JSON, a network refusal, an application permission error or a 120-second timeout.

The regression followed adding the parameterless `list_pages` tool first in the schema. Keeping the command/prompt composition unchanged but removing that tool, or moving `search_records` first, produced HTTP 200 with actual structured `search_records` arguments. Merely combining system messages did not cure the failure. The repaired production schema removes the redundant parameterless tool, retains the registered permission-scoped catalog in the prompt and `/ExecutiveAgent/Pages` API, and keeps `navigate_page` after the existing business tools. The installed model/parser pair remains prompt/schema sensitive; zero model errors are not guaranteed.

`ExecutiveModelException` now distinguishes configuration, connection, timeout/cancellation, HTTP, upstream tool parsing, malformed JSON, invalid/incomplete envelopes, truncated output, unsupported tools and invalid arguments. The production validator checks all calls in a response before any are dispatched. Generated call text is never parsed into an invocation. Production logs/audit retain categories, upstream status, attempt and response hash without raw record contents. Only the isolated harness enables request/response snapshots in ignored `App_Data/ExecutiveAgentTest/ollama`.

At most two retries apply to model generation only, for connection failures, timeouts and HTTP 408/429/502/503/504. The known parser HTTP 500, other HTTP 500, malformed JSON, invalid arguments, unsupported tools, permission denial and business execution are never retried. Negative fault-injection checks verify those bounds without fabricating any successful reply or invoking a dispatcher.

After repair the normal-session suite completed **83 passed / 0 failed** on `OrangeAuthCheck_Executive_20261116180240`, including actual English/Arabic tool chains, real weight request and signed approval execution, duplicates, stale state, authorization, CSRF, audit and draft-tender behavior. Separately: 11 negative failure/retry tests, 19 navigation matcher tests and 15 real controller-filter CSRF tests passed. Raw isolated exchanges confirm native `search_records` → `tender_status` → `prepare_action` calls with HTTP 200, not textual imitations. Hardware identity and a complete human microphone flow are separate checks.

The subsequent `-Browser` launch refused because the normal Visual Studio run occupied 5254/7182. This was a launch guard, not an integration-check failure. Visual Studio debugging was then stopped through its UI. To launch the retained tested build without rebuilding/reseeding, use `scripts/Test-ExecutiveAgent.ps1 -BrowserOnly` in normal PowerShell. That mode checks the matching saved full-suite result and build timestamp before starting. Browser readiness and the latest real voice/navigation checks must still be observed; a passed SQL suite alone is not browser readiness.
