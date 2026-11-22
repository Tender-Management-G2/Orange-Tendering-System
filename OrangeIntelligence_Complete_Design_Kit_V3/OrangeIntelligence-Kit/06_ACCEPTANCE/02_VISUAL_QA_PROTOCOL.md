# Fast visual QA — no endless test loops

For each implemented tab take **one 1440-wide desktop screenshot** showing that tab's meaningful default state; for Suppliers add **one selected supplier + one voice expanded shot**; add one narrow/mobile view and one light-theme view. Compare against the specific corresponding reference PNG, **not the image of another tab**. Report only three categories: visually correct, remaining visual gap, and functional blocker; each with a screenshot path.

When testing the network, record 8–12 seconds at idle and selected state to verify real orbit/physics; a static screenshot cannot prove animation. Check names/readability, click target sizes, cross-filter updates, inspector and table fallback. Measure actual frame responsiveness only if performance appears degraded; do not claim fixed latency without instrumentation.

Mandatory quick smoke: existing Sourcing Officer Radar route, one other internal role if permitted, denied Supplier, active table/chart, Graph selector, one AI tool query, one export. End by listing edited files and tests, maximum ~20 lines; **do not write 1000-line status reports**. Stop without commit.
