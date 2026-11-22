# Orange AI capability inventory

This inventory describes the existing floating AI Chat and its server-side tools. It is based on the routes, role rules, data services, and file handlers in the application. The assistant does not receive a general database, filesystem, or arbitrary URL tool.

## Current architecture

| Concern | Current implementation | Boundary |
| --- | --- | --- |
| Chat UI and history | `Views/Shared/_AiChatWidget.cshtml`, `wwwroot/js/ai-chat.js`, `AiChatController`, `AiChatService` | Conversation and files are scoped to the signed-in user. |
| AI provider | Shared `OllamaChatClient`, configured by the `Ollama` settings section; workflow requests reuse the existing Executive Agent client | The fixed data topics are parsed deterministically. Workflow proposals pass through the Executive Agent's fixed tool registry and server-side policy; the model never receives a database connection or arbitrary SQL tool. |
| Intent routing | `AssistantIntentParser` and `AssistantDataTools` | Fixed topic enum, bounded queries and exports; no generated SQL. |
| Authorization | `AssistantAccess`, current authenticated principal, active workspace role, row-scoped EF queries | The model and browser do not determine permissions. |
| Navigation | Tender record navigation through `AssistantActions`; general page navigation through `ExecutiveNavigationService` | The catalog is built from registered GET pages and the user's authenticated roles/authorization policies. LinkGenerator supplies internal paths; the model and client cannot provide a destination URL. |
| Mutations | `AssistantActions` tender draft conversation; prepared workflow actions from the existing Executive Agent | Tender draft creation is restricted to the active Sourcing Officer, gathers required fields and confirms before creation. Executive Agent workflow proposals require the existing review and passkey/password confirmation before execution. No direct publish, award, or delete tool is exposed. |
| Generated files | `AssistantDataset`, `AssistantExcel`, `AssistantPdf`, `AssistantWord`, `AssistantSlides`, `AiChatFileStore` | Real records are exported; files are stored outside `wwwroot`, owner-checked at download, audited and expired by a cleanup worker. |
| Upload analysis | `AiChatFileStore` extraction and `AiChatService` document context | PDF, XLSX, DOCX, CSV and TXT are accepted. Images, PPTX uploads, OCR/scanned PDFs and malware scanning are not configured in the chat pipeline. |
| Voice | Browser local speech recognition fallback; optional local `ExecutiveSpeechService` (whisper.cpp + FFmpeg); browser/system `speechSynthesis` | Arabic and English are selectable. Availability depends on installed local binaries/models or browser language packs and installed voices. There is no remote speech provider or guaranteed Jordanian-dialect support. |
| Audit and retention | `AuditLogs`, user-scoped conversation/file entities, `AiChatFileCleanupWorker` | File actions, data queries and sensitive assistant operations are logged; conversations are user-scoped and deletable. |

## Role-to-capability map

The actual role names are defined by `SystemRoles`: Admin, RequestingDepartment, SourcingOfficer, Supplier, SourcingManager, EvaluationCommittee, and TopCommittee. The active role is resolved from the authenticated principal and user record. A person with several roles uses the application's active-workspace ordering; Supplier is resolved separately.

| Active role | Read/query topics exposed by `AssistantDataTools.Allowed` | Record scope and notable limits |
| --- | --- | --- |
| SourcingOfficer | Tenders and tender details, deadlines, purchase requests, delayed requests, pending approvals, notifications, suppliers, supplier performance, activity, department spending, evaluation results; latest/search/draft/awaiting-approval/attachments tools | Sees tenders; request drafts are limited to own requests; create-tender draft requires the active officer workflow check. |
| SourcingManager | Tenders, deadlines, pending approvals, notifications, supplier performance, activity, department spending, evaluation results; latest/search/draft/awaiting-approval/attachments tools | Tender data is scoped to committee membership/responsibility. Financial visibility follows the existing workspace rule. |
| TopCommittee | Same read topics as SourcingManager | Tender data is scoped to Top Committee membership. |
| EvaluationCommittee | Tenders, deadlines, pending approvals, notifications, activity, evaluation results; latest/search/draft/awaiting-approval/attachments tools | Tender data is scoped to assigned Evaluation Committee membership. |
| RequestingDepartment | Tenders, purchase requests, delayed requests, deadlines, pending approvals, notifications, activity; latest/search/draft/awaiting-approval/attachments tools | Tender and request queries are scoped to the user's department. |
| Supplier | Own invitations, offers, deadlines, pending approvals, notifications, and latest tender invitation | Supplier rows are scoped by the authenticated user's SupplierId; no internal tender list or other suppliers' records. |
| Admin | Pending approvals, notifications, suppliers, activity | No internal tender rows or financial tender data are exposed by these assistant tools. |

These are assistant-specific read scopes, not replacements for controller authorization in the rest of the application. The controllers remain authoritative when a user follows a generated internal link.

## Supported actions and formats

| Capability | State |
| --- | --- |
| Query, summarize and filter authorized records | Implemented through fixed topics and bounded queries; parser coverage is deterministic and therefore narrower than unrestricted natural language. |
| Open latest or referenced tender | Implemented with server-resolved records and fixed internal route patterns. |
| Create tender draft | Implemented as a confirmation-gated conversation for the active Sourcing Officer and existing workflow. |
| Generate XLSX, PDF, DOCX, PPTX | Implemented from authorized datasets and document summaries; download requires ownership and an unexpired file record. |
| Generate CSV and TXT | Not currently exposed as assistant report formats. |
| Upload and analyze supported office/text documents | Implemented for PDF, XLSX, DOCX, CSV and TXT. Extracted text is bounded and treated as untrusted context. |
| Audio transcription and spoken answer | Implemented using optional local/browser components; test coverage and installed-provider availability must be verified in the deployment environment. |
| General page navigation | Implemented by resolving explicit open/go-to commands against the authorized page catalog shared with the existing Executive Agent navigation service. Ambiguous pages ask the user to choose; unmatched explicit navigation returns a bounded no-match response. |
| User profile management | Admin-only deterministic tools list/find/show users and can update name/phone/department or deactivate/reactivate through `AdminService.UpdateUserProfileAsync`, with confirmation, validation, audit and read-back. Admin accounts and the active Sourcing Officer uniqueness rule are protected. |
| Role changes | Chat only prepares a validated before/after summary and opens the existing protected Admin Roles page. It does not write role membership because strong re-authentication is not connected to this chat flow. `AdminService.AssignRoleAsync` also enforces the single-active-Sourcing-Officer constraint. |
| Persistent knowledge (RAG) | Added SQL Server document/chunk metadata, Ollama embeddings, hybrid semantic/keyword retrieval, per-user Personal scope, Admin-approved Shared/System scope, upload preview/save, review, replace, delete, re-index and citations. Shared and generated system knowledge remains unavailable to retrieval until an Admin approves it. |

## Not implemented or not verified

- Automated database-backed RAG isolation and persistence tests; those require a separately configured isolated SQL Server test database.
- Coverage of every authorized controller/page in the navigation catalog has not been independently verified.
- Model-provider-neutral orchestration and structured model tool calling; Ollama is the configured chat provider, while business tools remain deterministic server methods.
- Image/vision analysis, OCR, scanned-document support, uploaded PowerPoint parsing and malware scanning.
- CSV/TXT report generation, proactive suggestions, and saved report preferences.
- Full automated verification against SQL Server and live browser/provider checks in this environment. The AuthSmoke assistant suite requires a separately configured isolated SQL Server test database.

Do not describe an item in the final delivery as verified unless it has passed the corresponding application, provider, database, and authorization checks.

## Local AI runtime check (2026-11-20)

| Component | Configured model/runtime | GPU result | Notes |
| --- | --- | --- | --- |
| Existing chat and bid analysis | Ollama `gemma3:4b`; app requests `num_gpu=99`, context 4096 | Verified: `ollama ps` reported `100% GPU`, 2.9 GB model allocation | NVIDIA GeForce RTX 3070 Laptop GPU, 8 GB VRAM; Ollama 0.40.16. No CUDA/driver change was needed. |
| Existing Executive Agent | Ollama `qwen3.5:9b`; tool-calling request sets `think=false`, context 8192 | Verified: `ollama ps` reported `100% GPU`; API probe completed and reported 2 generated tokens | The short probe used context 512 and 12-token limit. This verifies placement, not representative latency or tool-call quality. |
| Knowledge embeddings | Ollama `bge-m3`; configured `num_gpu=99`; provider uses `/api/embed` | Verified: bilingual request returned 2 vectors × 1024 dimensions; `ollama ps` reported `100% GPU` | This verifies inference and device placement, not a measured latency improvement. Existing chunks must be indexed with this model; changing embedding models requires re-indexing. |
| Optional local speech recognition | `ExecutiveSpeechService` (whisper.cpp + FFmpeg), only if deployment binaries/model are configured | Not configured/verified here | Browser speech recognition is a browser capability and does not use the Ollama GPU. |
| Speech playback | Browser/system `speechSynthesis` | Not local model inference | No local TTS model was found in the application configuration. |
| OCR / vision | No configured local OCR or vision pipeline in the chat upload flow | Not applicable | Qwen-VL models are present in Ollama, but the application does not route uploads to them. |

Hardware probe: Windows, NVIDIA driver utility reported 621.24 (CUDA UMD 13.5), 8,192 MiB VRAM. A short local qwen inference reported about 6.4 GiB total GPU memory in use and 76% utilization while active. No controlled CPU-vs-GPU benchmark was performed, so no speedup is claimed. Ollama already selected GPU execution automatically; application configuration was left unchanged.
