# Declarative Generative UI — visual results inside AI conversation

The LLM NEVER returns executable JavaScript/HTML/SQL to embed. Model chooses from an allowlist of typed result views; server calculates/verifies values and authorization. Client renderer knows each component type. Text is sanitized; chart/table content references data returned by server tools.

Illustrative `AnalystMessage`:

```json
{
  "messageId":"m-0042",
  "role":"assistant",
  "text":"Supplier A21 has the highest observed participation within the selected scope.",
  "visuals":[
    {"kind":"kpi_set","title":"Participation","items":[{"label":"A21","value":5},{"label":"A22","value":4}]},
    {"kind":"bar_chart","title":"Closed rounds with submitted offers","categoryField":"supplier","valueField":"rounds","dataRef":"authorized-tool-result-239"},
    {"kind":"data_table","columns":["supplier","rounds"],"dataRef":"authorized-tool-result-239"}
  ],
  "provenance":{"tool":"GetSupplierParticipation","scopeSummary":"Current user, closed rounds","asOf":"server timestamp"},
  "actions":[{"kind":"export","format":"xlsx","dataRef":"authorized-tool-result-239"}]
}
```

Allowed `kind`: `kpi_set`, `bar_chart`, `line_chart`, `scatter_chart`, `donut_chart`, `data_table`, `timeline`, `network_focus`, `evidence_list`, `file_preview`, `recommendations`, `empty_state`. Add types only if actual validated UI renderer requires them. User asks for custom comparisons: compute trusted tool data, then choose chart. Out-of-scope or unverified claims must not be voiced as fact.

Schema validation: known fields only; row count/labels/currency constraints; maximum supported plot points; no model-provided URLs unless verified. All `dataRef`s point to authorized server-cached reads or tool-result IDs with expiry and are rechecked at export time. Never allow client to mutate `departmentId`, userId or a scope outside authorization.

UI: a clear inline card with readable chart; buttons `View table`, `Source`, `Export Excel`, `Export PDF`, `Pin insight`, and a compact follow-up prompt. Desktop 2-column chat + data panel optional; mobile single-column. No use of untrusted charts embedded as HTML.
