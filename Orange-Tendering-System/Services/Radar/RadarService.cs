using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Orange_Tendering_System.Services.Radar;

public sealed record TimelineEvent(DateTime At, string Type, string Label, IReadOnlyList<string> NodeIds, IReadOnlyList<string> EdgeIds);
public sealed record DocumentDnaState(string State, int BidDocuments, int Readable, int Matches, string? Message, DateTime? ComputedAt);

public sealed record RadarWorkspace(
    RadarAnalytics.MarketSummary Summary,
    IReadOnlyDictionary<string, string> Definitions,
    IReadOnlyList<RadarAnalytics.SupplierStats> Suppliers,
    IReadOnlyList<RadarAnalytics.SupplierDna> Dna,
    RadarAnalytics.Competition Competition,
    IReadOnlyList<RadarAnalytics.AwardDistribution> Awards,
    IReadOnlyList<RadarAnalytics.ItemComparison> Items,
    IReadOnlyList<RadarAnalytics.RelativePosition> Positions,
    IReadOnlyList<RadarFinding> Findings,
    IReadOnlyList<DetectorStatus> Detectors,
    RadarGraphResult Graph,
    IReadOnlyList<TimelineEvent> Timeline,
    DocumentDnaState DocumentDna,
    RadarThresholds Thresholds);

// Facade used by the controller, the AI analyst tools, the briefing and the report, so all of them read the same numbers.
public sealed class RadarService(RadarDataService data, RadarDocumentDna dna, IMemoryCache cache, IOptions<RadarOptions> options, ILogger<RadarService> logger)
{
    public RadarThresholds Thresholds => options.Value.Thresholds;

    public Task<RadarSnapshot> SnapshotAsync(RadarPeriod period, CancellationToken ct) => data.LoadAsync(period, ct);

    private static string SemanticKey(RadarPeriod p) => $"orange-radar:semantic:{p.From:O}:{p.To:O}";

    public async Task<RadarWorkspace> WorkspaceAsync(RadarPeriod period, CancellationToken ct)
    {
        var s = await data.LoadAsync(period, ct);
        var fingerprints = dna.Fingerprints(s);
        cache.TryGetValue(SemanticKey(period), out (IReadOnlyList<SemanticMatch> Matches, DocumentDnaState State) semantic);
        var forensic = RadarForensics.Detect(s, Thresholds, fingerprints, semantic.Matches, semantic.State?.State == "unavailable" ? "unavailable" : null);
        var stats = RadarAnalytics.Suppliers(s);
        var max = stats.Count == 0 ? 0 : stats.Max(x => x.Participations);
        var graph = RadarGraph.Build(s, forensic.Findings);
        return new RadarWorkspace(RadarAnalytics.Summary(s), RadarAnalytics.Definitions, stats, stats.Select(x => RadarAnalytics.Dna(x, max)).ToList(),
            RadarAnalytics.CompetitionAnalysis(s, Thresholds.MinimumBidsForCompetition), RadarAnalytics.AwardDistributions(s), RadarAnalytics.ItemComparisons(s),
            RadarAnalytics.RelativePositions(s), forensic.Findings, forensic.Detectors, graph, Timeline(s, graph),
            semantic.State ?? new DocumentDnaState("notRun", s.Documents.Count(d => d.Owner == "Bid"), 0, 0, null, null), Thresholds);
    }

    public async Task<ForensicResult> ForensicsAsync(RadarPeriod period, CancellationToken ct)
    {
        var s = await data.LoadAsync(period, ct);
        cache.TryGetValue(SemanticKey(period), out (IReadOnlyList<SemanticMatch> Matches, DocumentDnaState State) semantic);
        return RadarForensics.Detect(s, Thresholds, dna.Fingerprints(s), semantic.Matches);
    }

    // Embeds readable bid documents (cached per text hash) and stores the semantic matches for this period.
    public async Task<DocumentDnaState> RunDocumentDnaAsync(RadarPeriod period, CancellationToken ct)
    {
        var s = await data.LoadAsync(period, ct);
        DocumentDnaState state;
        IReadOnlyList<SemanticMatch>? matches = null;
        try
        {
            var result = await dna.SemanticMatchesAsync(s, ct);
            matches = result.Matches;
            state = new DocumentDnaState("ok", result.Documents, result.Readable, result.Matches.Count(m => m.Similarity >= Thresholds.SemanticSimilarity),
                result.Readable < 2 ? "Fewer than two readable bid documents (TXT, CSV or DOCX) in this period." : null, DateTime.UtcNow);
        }
        catch (RadarAiUnavailableException e)
        {
            logger.LogInformation("Document DNA embeddings unavailable: {Message}", e.Message);
            state = new DocumentDnaState("unavailable", s.Documents.Count(d => d.Owner == "Bid"), 0, 0, e.Message, DateTime.UtcNow);
        }
        cache.Set(SemanticKey(period), (matches, state), TimeSpan.FromMinutes(20));
        return state;
    }

    // Historical replay events. Offers appear when their round's bidding closed (sealing), never at their private submission time.
    private static List<TimelineEvent> Timeline(RadarSnapshot s, RadarGraphResult graph)
    {
        var events = new List<TimelineEvent>();
        foreach (var r in s.AnalyzedRounds)
        {
            var bids = s.Bids.Where(b => b.RoundId == r.Id).ToList();
            if (bids.Count == 0) continue;
            var tender = s.Tender(r.TenderId);
            events.Add(new TimelineEvent(r.AvailableAt, "round", $"{tender?.Title} · round {r.RoundNumber}: {bids.Count} offers opened",
                bids.Select(b => $"s-{b.SupplierId}").Append($"t-{r.TenderId}").Distinct().ToList(),
                bids.Select(b => $"e-participation-s-{b.SupplierId}-t-{r.TenderId}").Distinct().ToList()));
        }
        foreach (var a in RadarAnalytics.Awards(s))
            events.Add(new TimelineEvent(a.AwardedAt ?? s.Round(a.RoundId)!.AvailableAt, "award", $"{a.Tender} awarded to {a.Supplier}", [$"s-{a.SupplierId}", $"t-{a.TenderId}"], [$"e-award-s-{a.SupplierId}-t-{a.TenderId}"]));
        foreach (var e in graph.Edges.Where(e => e.Kind != "fact" && e.AvailableAt != null))
            events.Add(new TimelineEvent(e.AvailableAt!.Value, "pattern", $"Pattern established: {e.Label}", [e.Source, e.Target], [e.Id]));
        return events.OrderBy(e => e.At).ToList();
    }
}
