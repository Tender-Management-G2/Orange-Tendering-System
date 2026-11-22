# Document Intelligence — Excel and PDF, within AI Studio

1. User uploads PDF/XLSX to a **temporary secured analysis workspace**. No direct domain table change. Reject unsafe types/size/paths.
2. Extract content with existing .NET libraries if available. XLSX: sheets, headers, types, formulas vs cached values, amounts/currencies, row counts. PDF: selectable text/tables/pages; scanned content should say OCR needed/unsupported unless OCR is present and tested.
3. Present extraction preview with source citations to file/page/sheet and confidence caveats; user can correct extracted column meaning.
4. Ask AI to summarize/compare; AI selects safe chart/table layout but trusted server code performs numerical operations. Inputs from document are **untrusted user-supplied**, distinct from verified procurement records. User must opt in before joining with system data.
5. Download Excel/PDF reports from current authorized session, with correct data scopes and no Excel formula injection.
6. Remove uploaded files according to project policy or session cleanup; do not log document contents or keys.

Avoid conflating byte-identical document detection with PDF content extraction. Preserve whichever prior extraction services already work.
