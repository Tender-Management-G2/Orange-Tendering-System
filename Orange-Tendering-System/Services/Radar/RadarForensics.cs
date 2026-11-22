using System.Security.Cryptography;
using System.Text;

namespace Orange_Tendering_System.Services.Radar;

public sealed record EvidenceRef(string Type, int Id, string Label);

// Level: "Context" = factual relationship worth knowing; "ReviewSignal" = a statistical pattern a person may review.
// Neither level is an accusation: every finding carries its method, sample size and limitations.
public sealed record RadarFinding(string Id, string Detector, string Title, string Level, IReadOnlyList<int> SupplierIds, IReadOnlyList<int> TenderIds,
    IReadOnlyList<int> RoundIds, IReadOnlyList<EvidenceRef> Records, IReadOnlyDictionary<string, string> Metrics, int SampleSize, string Method,
    string Period, string Limitations, string Explanation, DateTime AvailableAt, string? AttributeKey = null, string? AttributeLabel = null);

public sealed record DetectorStatus(string Detector, string Label, string State, int Findings, string Note, string Threshold);

public sealed record DocFingerprint(int DocumentId, string FileName, string Owner, int? SupplierId, int? BidId, int? TenderId, int? RoundId,
    string? Sha256, string ExtractionState, DateTime AvailableAt);

public sealed record SemanticMatch(int DocumentA, int DocumentB, int SupplierA, int SupplierB, double Similarity, string ExcerptA, string ExcerptB,
    int ChunksCompared, int TemplateChunksExcluded, DateTime AvailableAt);

public sealed record ForensicResult(IReadOnlyList<RadarFinding> Findings, IReadOnlyList<DetectorStatus> Detectors);

public static class RadarForensics
{
    public const string JointParticipation = "joint-participation", PriceGap = "price-gap", SubmissionTiming = "submission-timing";

    public static ForensicResult Detect(RadarSnapshot s, RadarThresholds t)
    {
        var findings = new List<RadarFinding>();
        var statuses = new List<DetectorStatus>();
        void Add(string detector, string label, IEnumerable<RadarFinding> found, string threshold, Func<int, (string State, string Note)> state)
        {
            var list = found.ToList(); findings.AddRange(list);
            var (st, note) = state(list.Count);
            statuses.Add(new DetectorStatus(detector, label, st, list.Count, note, threshold));
        }

        var joint = RadarAnalytics.JointRounds(s);
        Add(JointParticipation, "Repeated joint participation", Joint(s, t, joint), $"{t.JointParticipationMinRounds}+ shared closed rounds (context only, never a signal by itself)",
            n => joint.Count == 0 ? ("insufficient", "No closed round had two or more offers.") : ("ok", $"{joint.Count} supplier pairs met in at least one closed round."));

        var gapPairs = joint.Count(p => p.Value.Count >= t.PriceGapMinRounds);
        Add(PriceGap, "Repeated relative price gap", PriceGaps(s, t, joint), $"{t.PriceGapMinRounds}+ shared rounds, same direction, ratio {t.PriceGapMinRatio:0.###}-{t.PriceGapMaxRatio:0.##}, variation <= {t.PriceGapMaxVariation:P1}",
            n => gapPairs == 0 ? ("insufficient", $"No supplier pair shared {t.PriceGapMinRounds}+ priced rounds.") : ("ok", $"{gapPairs} pairs had enough shared rounds."));

        Add(SubmissionTiming, "Submission-time proximity", Timing(s, t, joint), $"Submitted within {t.TimingWindowMinutes} min of each other in {t.TimingMinRounds}+ rounds",
            n => s.Bids.Count(b => b.SubmittedAt != null) < 2 ? ("insufficient", "Too few timestamped offers.") : ("ok", $"{s.Bids.Count(b => b.SubmittedAt != null)} timestamped offers."));

        return new ForensicResult(findings.OrderBy(f => f.Level == "ReviewSignal" ? 0 : 1).ThenByDescending(f => f.SampleSize).ToList(), statuses);
    }

    // Repeated joint participation. In a small market ordinary competitors meet in most rounds, so this is always context:
    // it explains why suppliers are connected and feeds the price and timing detectors, but never becomes a signal on its own.
    private static IEnumerable<RadarFinding> Joint(RadarSnapshot s, RadarThresholds t, Dictionary<(int A, int B), List<int>> joint)
    {
        var roundsBySupplier = s.Bids.GroupBy(b => b.SupplierId).ToDictionary(g => g.Key, g => g.Select(b => b.RoundId).Distinct().Count());
        foreach (var ((a, b), rounds) in joint.Where(p => p.Value.Count >= t.JointParticipationMinRounds))
        {
            var union = roundsBySupplier[a] + roundsBySupplier[b] - rounds.Count;
            var overlap = union == 0 ? 0 : (double)rounds.Count / union;
            yield return new RadarFinding(Id(JointParticipation, a, b), JointParticipation, "Repeated joint participation", "Context",
                [a, b], TenderIds(s, rounds), rounds, Rounds(s, rounds),
                new Dictionary<string, string> { ["sharedRounds"] = rounds.Count.ToString(), ["roundsA"] = roundsBySupplier[a].ToString(), ["roundsB"] = roundsBySupplier[b].ToString(), ["overlap"] = overlap.ToString("P0") },
                rounds.Count, "Count of closed rounds in which both suppliers submitted an offer; overlap = shared rounds / rounds where either bid (Jaccard).",
                s.Period.Label, "Suppliers in the same category are expected to meet often; small markets inflate overlap.",
                $"{s.SupplierName(a)} and {s.SupplierName(b)} both submitted offers in {rounds.Count} closed rounds ({overlap:P0} of the rounds either of them bid in).",
                rounds.Select(r => s.Round(r)!.AvailableAt).Max());
        }
    }

    // Repeated relative price gap: one supplier's total sits a near-constant small margin above the other's.
    private static IEnumerable<RadarFinding> PriceGaps(RadarSnapshot s, RadarThresholds t, Dictionary<(int A, int B), List<int>> joint)
    {
        foreach (var ((a, b), rounds) in joint.Where(p => p.Value.Count >= t.PriceGapMinRounds))
        {
            var samples = rounds.Select(r => (Round: r, A: s.Bids.First(x => x.RoundId == r && x.SupplierId == a), B: s.Bids.First(x => x.RoundId == r && x.SupplierId == b)))
                .Where(x => RadarBehavior.CompatiblePrice(x.A, x.B)).ToList();
            if (samples.Count < t.PriceGapMinRounds) continue;
            var ratios = samples.Select(x => (double)(x.B.Total / x.A.Total)).ToList();
            bool higherB = ratios.All(r => r > 1), higherA = ratios.All(r => r < 1);
            if (!higherA && !higherB) continue;
            var oriented = higherB ? ratios : ratios.Select(r => 1 / r).ToList();
            var mean = oriented.Average();
            var cv = Math.Sqrt(oriented.Sum(r => (r - mean) * (r - mean)) / oriented.Count) / mean;
            if (mean < t.PriceGapMinRatio || mean > t.PriceGapMaxRatio || cv > t.PriceGapMaxVariation) continue;
            var (low, high) = higherB ? (a, b) : (b, a);
            var ids = samples.Select(x => x.Round).ToList();
            yield return new RadarFinding(Id(PriceGap, a, b), PriceGap, "Repeated relative price gap", "ReviewSignal", [a, b], TenderIds(s, ids), ids,
                samples.SelectMany(x => new[] { new EvidenceRef("Bid", x.A.Id, $"{s.SupplierName(a)} {x.A.Currency} {x.A.Total:N2}"), new EvidenceRef("Bid", x.B.Id, $"{s.SupplierName(b)} {x.B.Currency} {x.B.Total:N2}") }).ToList(),
                new Dictionary<string, string> { ["meanGap"] = (mean - 1).ToString("P2"), ["variation"] = cv.ToString("P2"), ["rounds"] = samples.Count.ToString(), ["higher"] = s.SupplierName(high) },
                samples.Count, "Ratio of the two offer totals per shared round (same round, currency, recorded items, quantities and units, latest revision); flagged when the direction never changes and the ratio barely varies.",
                s.Period.Label, "Similar cost structures, shared distributors or price lists can produce stable gaps. Offer totals include extra costs.",
                $"In {samples.Count} shared rounds {s.SupplierName(high)} priced {mean - 1:P1} above {s.SupplierName(low)} on average, with only {cv:P1} variation.",
                ids.Select(r => s.Round(r)!.AvailableAt).Max());
        }
    }

    // Repeated submission-time proximity.
    private static IEnumerable<RadarFinding> Timing(RadarSnapshot s, RadarThresholds t, Dictionary<(int A, int B), List<int>> joint)
    {
        foreach (var ((a, b), rounds) in joint.Where(p => p.Value.Count >= t.TimingMinRounds))
        {
            var close = rounds.Select(r => (Round: r, A: s.Bids.First(x => x.RoundId == r && x.SupplierId == a), B: s.Bids.First(x => x.RoundId == r && x.SupplierId == b)))
                .Where(x => x.A.SubmittedAt != null && x.B.SubmittedAt != null)
                .Select(x => (x.Round, x.A, x.B, Minutes: Math.Abs((x.A.SubmittedAt!.Value - x.B.SubmittedAt!.Value).TotalMinutes)))
                .Where(x => x.Minutes <= t.TimingWindowMinutes).ToList();
            if (close.Count < t.TimingMinRounds) continue;
            var ids = close.Select(x => x.Round).ToList();
            yield return new RadarFinding(Id(SubmissionTiming, a, b), SubmissionTiming, "Offers repeatedly submitted minutes apart", "ReviewSignal", [a, b], TenderIds(s, ids), ids,
                close.SelectMany(x => new[] { new EvidenceRef("Bid", x.A.Id, $"{s.SupplierName(a)} {x.A.SubmittedAt:yyyy-MM-dd HH:mm}"), new EvidenceRef("Bid", x.B.Id, $"{s.SupplierName(b)} {x.B.SubmittedAt:yyyy-MM-dd HH:mm}") }).ToList(),
                new Dictionary<string, string> { ["closeRounds"] = close.Count.ToString(), ["sharedRounds"] = rounds.Count.ToString(), ["medianGapMinutes"] = Median(close.Select(x => x.Minutes)).ToString("0.0") },
                rounds.Count, $"Absolute difference between the submission times of the latest revisions; counted when within {t.TimingWindowMinutes} minutes.",
                s.Period.Label, "Many suppliers submit just before a deadline, so proximity near a deadline is common and weak evidence on its own.",
                $"In {close.Count} of {rounds.Count} shared rounds {s.SupplierName(a)} and {s.SupplierName(b)} submitted within {t.TimingWindowMinutes} minutes of each other.",
                ids.Select(r => s.Round(r)!.AvailableAt).Max());
        }
    }

    private static List<int> TenderIds(RadarSnapshot s, IEnumerable<int> rounds) => rounds.Select(r => s.Round(r)!.TenderId).Distinct().ToList();
    private static List<EvidenceRef> Rounds(RadarSnapshot s, IEnumerable<int> rounds) =>
        rounds.Select(r => { var round = s.Round(r)!; return new EvidenceRef("Round", r, $"{s.Tender(round.TenderId)?.Title} · round {round.RoundNumber}"); }).ToList();
    private static double Median(IEnumerable<double> values) { var v = values.Order().ToArray(); return v.Length == 0 ? 0 : v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2; }

    // Stable identifiers: the same evidence always produces the same finding id, so links, briefings and reports stay valid.
    public static string Id(string detector, params object[] key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(detector + "|" + string.Join("|", key.Select(k => k is int[] a ? string.Join(",", a) : k.ToString()))));
        var code = detector switch { JointParticipation => "JP", PriceGap => "PG", _ => "ST" };
        return $"RF-{code}-{Convert.ToHexString(hash)[..8]}";
    }
}
