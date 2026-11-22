using Microsoft.Extensions.Options;

namespace Orange_Tendering_System.Services.Radar;

public sealed record TimelineEvent(DateTime At, string Type, string Label, IReadOnlyList<string> NodeIds, IReadOnlyList<string> EdgeIds);

public sealed record RadarWorkspace(
    RadarAnalytics.MarketSummary Summary,
    IReadOnlyDictionary<string, string> Definitions,
    IReadOnlyList<RadarAnalytics.SupplierStats> Suppliers,
    RadarAnalytics.Competition Competition,
    IReadOnlyList<RadarAnalytics.AwardDistribution> Awards,
    IReadOnlyList<RadarAnalytics.ItemComparison> Items,
    IReadOnlyList<RadarAnalytics.RelativePosition> Positions,
    IReadOnlyList<RadarFinding> Findings,
    IReadOnlyList<DetectorStatus> Detectors,
    RadarGraphResult Graph,
    IReadOnlyList<TimelineEvent> Timeline,
    RadarThresholds Thresholds,
    RadarNetwork Network,
    IReadOnlyList<TenderLifecycle> Tenders,
    IReadOnlyList<BehaviorObservation> Behavior,
    string Scope);

// Facade used by the controller, the AI analyst tools, the briefing and the report, so all of them read the same numbers.
public sealed class RadarService(RadarDataService data, IOptions<RadarOptions> options)
{
    public RadarThresholds Thresholds => options.Value.Thresholds;

    public Task<RadarSnapshot> SnapshotAsync(RadarPeriod period, CancellationToken ct) => data.LoadAsync(period, ct);

    public async Task<RadarWorkspace> WorkspaceAsync(RadarPeriod period, CancellationToken ct)
    {
        var s = await data.LoadAsync(period, ct);
        var forensic = RadarForensics.Detect(s, Thresholds);
        var stats = RadarAnalytics.Suppliers(s);
        var graph = RadarGraph.Build(s);
        return new RadarWorkspace(RadarAnalytics.Summary(s), RadarAnalytics.Definitions, stats,
            RadarAnalytics.CompetitionAnalysis(s, Thresholds.MinimumBidsForCompetition), RadarAnalytics.AwardDistributions(s), RadarAnalytics.ItemComparisons(s),
            RadarAnalytics.RelativePositions(s), forensic.Findings, forensic.Detectors, graph, Timeline(s, graph), Thresholds,
            RadarGraph.Network(s, Thresholds.CompetitionLinkMinRounds), RadarTenders.Build(s), RadarBehavior.Build(s), s.Scope.Label);
    }

    public async Task<ForensicResult> ForensicsAsync(RadarPeriod period, CancellationToken ct)
    {
        var s = await data.LoadAsync(period, ct);
        return RadarForensics.Detect(s, Thresholds);
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
            events.Add(new TimelineEvent(r.AvailableAt, "round", $"{tender?.Title} · {RadarOfferComparison.CycleLabel(r.RoundNumber)}: {bids.Count} offers opened",
                bids.Select(b => $"s-{b.SupplierId}").Append($"t-{r.TenderId}").Distinct().ToList(),
                bids.Select(b => $"e-participation-s-{b.SupplierId}-t-{r.TenderId}").Distinct().ToList()));
        }
        foreach (var a in RadarAnalytics.Awards(s))
            events.Add(new TimelineEvent(a.AwardedAt ?? s.Round(a.RoundId)!.AvailableAt, "award", $"{a.Tender} awarded to {a.Supplier}", [$"s-{a.SupplierId}", $"t-{a.TenderId}"], [$"e-award-s-{a.SupplierId}-t-{a.TenderId}"]));
        return events.OrderBy(e => e.At).ToList();
    }
}
