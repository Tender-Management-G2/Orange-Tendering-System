# Forensics — a casework experience, not eight detector cards

Top: four compact KPIs (findings, affected entities, evidence, coverage), all filtered by authorization and analysis status. Primary center: a **finding selector + tailored visualization** + evidence inspector. A finding can display one of several graph grammars: document-to-suppliers star, submissions-over-time timeline, price gap comparison, category concentration, recurrent bidding network. Do NOT show the 3D Supplier Universe again.

Evidence model: `FindingId`, `DetectorType`, `EntityRefs`, `SourceRecordRefs`, `Method`, `SampleSize`, `ObservedPeriod`, `AlternativeExplanation`, `Status = review signal/context/insufficient/not run`. Each link must explain cause and source. One file shared by N suppliers -> N file-supplier links by default; do not clutter graph with N×(N−1)/2 all-to-all edges.

Text rules: `Potential pattern for human review`, not `fraud detected`. Shared contacts, similar bids, recurring participation and timing can have ordinary explanations. No accused or guilty labels, no fabricated confidence.

Action: pin finding into current report; inspect records authorized server-side; ask the same global AI to explain with evidence. No changing supplier approval or award decisions from this screen.
