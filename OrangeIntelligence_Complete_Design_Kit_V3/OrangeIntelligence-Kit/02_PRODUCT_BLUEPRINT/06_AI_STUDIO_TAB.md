# AI Studio — custom insights rendered INSIDE the chat

Three regions, not separate full pages: conversation thread, dynamic result area (can be inline messages), and optional compact provenance/file controls. User asks "Compare A21, A22, A24 participation" -> authorized analysis tool returns data and schema -> UI renders a comparison card + bar/table **inside the conversation** -> export (CSV/XLSX/PDF when supported). A chart elsewhere in the dashboard does NOT satisfy this requirement.

Upload PDF/XLSX: validate type/size, store temp securely, extract actual data, allow preview/correction, distinguish `uploaded document` from official `system records`; no unsolicited persistence to domain tables. PDF images may require OCR, show unsupported extraction honestly. Return file citations/page/sheet where possible. Avoid allowing an uploaded file to instruct the AI to disregard permissions.

Reuse the current general chatbot rather than create a competing one. One persistent text/audio conversation; per-tab context, server-side authorized tool calls, no ability to make procurement decisions or state changes. Recommendations must remain advisory.

Example result schema described in `04_ENGINEERING/04_GENERATIVE_UI_SCHEMA.md`. No raw model HTML/JS or arbitrary SQL executed from prompt.
