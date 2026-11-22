# Security — permission gates for every analysis surface

Official AI #1 permits all internal roles, excludes Suppliers. This is **not** blanket permission to all tender data. Use existing Identity roles & policies. Permissions attach to each dataset and each tool call. When a tender is inaccessible, it must be excluded from search, charts, counts, AI context, direct endpoints, export and file access alike.

Backend first: authentication -> role/department/company/tender predicates -> authorized records -> computed DTOs -> verified AI tools -> declarative UI -> export authorization. The model is not responsible for access decisions. Validate each `tenderId`, `supplierId`, `documentId`, graph edge evidence and date filter. The client should not infer hidden rows from `total count` leakage or error messages.

**Sealed bids:** not sent, not used in aggregate and not included in report while sealed; show a safe count only if the role is permitted to know the count. Official awards vs recommendations are separate flags. All tool methods must be read-only in this Intelligence surface. No CreateUser/DeleteSupplier/RejectBid/AwardTender actions here, even if general AI agent supports them elsewhere.

PDF/XLSX upload defenses: file extensions & MIME/content checks, size limits, virus scan if available, temporary storage isolation, neutralized spreadsheet formulas for export, PDF text prompt-injection isolation, no exposing local filesystem path, safe retention and deletion. Distinguish user-uploaded untrusted content from application truth. Apply scope to generated exports and report download endpoints, not just the screen.

Tests: signed-out blocked; supplier blocked; authorized internal users see only their scope; file link direct URL blocked when unauthorized; forge AI tool arguments rejected; prompt injection in file is treated as data not instruction; seal rule preserved. OWASP LLM Top 10 is a security checklist, not a substitute for project-specific authorization.
