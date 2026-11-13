# Actual browser E2E evidence

Open the locally generated `report.html`. Screenshots are in `screenshots/`, sanitized actual output in `logs/`, final counts in `summary.json`, and final/attempt-level records in `results.json`. Generated artifacts are intentionally ignored by Git to prevent accidentally publishing sensitive browser evidence. Nothing was pushed or merged.

Testing branch: `codex/real-browser-e2e-20261113`.

The browser deployment is the actual Release application running on Kestrel at `https://localhost:7443`, with SQL Server database `OrangeAuthCheck_BrowserE2E_20261113`. All nine historical EF migrations were used. Production `OrangeTenderingSystem` was only backed up and queried read-only; its user/supplier/tender/bid/document counts remain zero.

The original project/private documents, prior local edits, Git history and verified database backup are preserved under `C:\Users\user\Desktop\Orange-browser-e2e-backup-20261113T124432Z`. The project archive SHA-256 is recorded in that directory's manifest. The SQL backup checksum was verified again after testing. Test credentials, cookies, tokens and synthetic documents stay outside this repository, in the private fixture/runtime directory. Do not copy `private-state.json` into evidence or source control.

Fourteen test accounts were provisioned through actual Identity services: Admin, Officer, Requesting Department, Manager, three Evaluation Committee users, four Top Committee users and three Suppliers. Fixture-issued employee activation/OTP/reset tokens were consumed by real browser pages; this is explicitly **not** proof that an email was delivered. Supplier approval, committee proposals, scoring, signatures and award used the real authorized UI. No application service or clock was replaced in that deployment.

`BrowserProvisioning.cs` is a mode of the existing authentication harness, guarded to the isolated browser database prefix. It is not a restored workflow test project. Use `ORANGE_E2E_PRIVATE_STATE` to point to the protected fixture state outside the workspace. The browser scripts here record this run's phased execution and fixture IDs; they are diagnostic evidence scripts, not a claim of a single-command general CI suite. Preserve logs of failed attempts and reruns. The report aggregator derives outcomes from those actual logs, not from an authored list of expected passes.

The separate existing authentication harness passed 142 checkpoints, and the existing external archived regression copy passed 35 tests, including SQL-backed integration/upgrade checks and pure guard/calculation checks. Those regression suites use captured email or substituted external HTTP and are **separate from real-browser counts**. The removed workflow project was not recreated in the workspace or solution.

Blocked integrations/workflows are listed explicitly in the report: live Gmail SMTP/OTP and Forgot Password delivery, live employee activation-email invitation, supplier self-registration requiring SMTP, and Zoom API creation. The schedule used a test-format Zoom URL, not a verified live meeting. Live Ollama `gemma3:4b` analyzed the actual stored synthetic bids successfully. Current report signing is electronic acknowledgment; no trusted-certificate signing success is claimed. Face ID remains disabled/incomplete.

Fixes on this branch wire the previously disabled Purchase Request submit/review and Admin account-management paths, correct boolean reactivation form rendering/model validation, and fix literal Razor version text on report pages. The tested Purchase Request path submits a single line item; broader request editing/resubmission and broader Admin role-editing functionality are not claimed as tested or complete.
