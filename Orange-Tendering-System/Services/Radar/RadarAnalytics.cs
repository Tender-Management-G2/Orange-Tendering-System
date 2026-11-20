using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Radar;

// Market calculations for ORANGE RADAR. Pure functions over a RadarSnapshot: C# is the only source of numbers;
// the AI analyst and the briefing narrate these values but never compute them.
public static class RadarAnalytics
{
    public static readonly IReadOnlyDictionary<string, string> Definitions = new Dictionary<string, string>
    {
        ["registeredSuppliers"] = "Every supplier company recorded in the system, whatever its registration status.",
        ["eligibleSuppliers"] = "Suppliers whose registration is Approved (they can be invited).",
        ["participatingSuppliers"] = "Suppliers with at least one submitted offer in an analysed (closed) round of the period.",
        ["analyzedTenders"] = "Tenders with at least one round whose bidding has closed in the period.",
        ["submittedBids"] = "Effective offers: the latest submitted revision per invitation, in closed rounds only. Sealed offers are excluded.",
        ["avgBidsPerRound"] = "Submitted offers divided by analysed rounds.",
        ["awardedValue"] = "Sum of officially awarded offer totals (items + extra costs), per currency. Historical award value, not payments.",
        ["winRate"] = "Official awards won divided by awarded tenders the supplier bid on. Undefined when the supplier bid on no awarded tender.",
        ["participationRate"] = "Rounds with a submitted offer divided by rounds the supplier was invited to (closed rounds only).",
        ["relativePrice"] = "Offer total divided by the median offer total of the same round and currency, minus one. Needs two comparable offers.",
        ["reviewSignals"] = "Detected patterns that a person may want to review. A signal is not evidence of wrongdoing."
    };

    public sealed record CurrencyValue(string Currency, decimal Value, int Count);

    public sealed record MarketSummary(int RegisteredSuppliers, int EligibleSuppliers, int ParticipatingSuppliers, int AnalyzedTenders, int AnalyzedRounds,
        int Invitations, int SubmittedBids, double? AvgBidsPerRound, double? AvgBidsPerTender, double? ResponseRate, IReadOnlyList<CurrencyValue> AwardedValue,
        int Awards, int SealedRounds, int SealedBids, string Period, DateTime GeneratedAt);

    public static MarketSummary Summary(RadarSnapshot s)
    {
        var rounds = s.AnalyzedRounds.ToList();
        var roundIds = rounds.Select(r => r.Id).ToHashSet();
        var invitations = s.Invitations.Count(i => roundIds.Contains(i.RoundId));
        var tenders = rounds.Select(r => r.TenderId).Distinct().Count();
        var awards = Awards(s);
        return new MarketSummary(
            s.Suppliers.Count, s.Suppliers.Count(x => x.Status == SupplierStatus.Approved), s.Bids.Select(b => b.SupplierId).Distinct().Count(),
            tenders, rounds.Count, invitations, s.Bids.Count,
            rounds.Count == 0 ? null : Math.Round((double)s.Bids.Count / rounds.Count, 2),
            tenders == 0 ? null : Math.Round((double)s.Bids.Count / tenders, 2),
            invitations == 0 ? null : Math.Round((double)s.Bids.Count / invitations, 3),
            awards.GroupBy(a => a.Currency).Select(g => new CurrencyValue(g.Key, g.Sum(a => a.Value), g.Count())).OrderByDescending(x => x.Value).ToList(),
            awards.Count, s.SealedRounds, s.SealedBids, s.Period.Label, s.GeneratedAt);
    }

    public sealed record Award(int TenderId, string Tender, int RoundId, int BidId, int SupplierId, string Supplier, string Department, string Currency, decimal Value, DateTime? AwardedAt);

    public static List<Award> Awards(RadarSnapshot s)
    {
        var bids = s.Bids.ToDictionary(b => b.Id);
        return s.Tenders.Where(t => t.AwardedBidId is { } id && bids.ContainsKey(id))
            .Select(t => { var b = bids[t.AwardedBidId!.Value]; return new Award(t.Id, t.Title, b.RoundId, b.Id, b.SupplierId, s.SupplierName(b.SupplierId), t.Department, b.Currency, b.Total, t.AwardedAt); })
            .OrderBy(a => a.AwardedAt).ToList();
    }

    // ---------- Supplier landscape and DNA ----------

    public sealed record SupplierStats(int SupplierId, string Name, string Category, string Status, int Invitations, int Participations, int Tenders,
        int AwardedTendersBid, int Wins, double? WinRate, double? ParticipationRate, IReadOnlyList<CurrencyValue> AwardedValue,
        double? RelativePrice, int PricedRounds, int ComplianceChecked, int Compliant, int Documents, int VerifiedDocuments, DateTime? FirstActivity, DateTime? LastActivity);

    public static List<SupplierStats> Suppliers(RadarSnapshot s)
    {
        var analyzed = s.AnalyzedRounds.Select(r => r.Id).ToHashSet();
        var awards = Awards(s);
        var awardedTenders = awards.Select(a => a.TenderId).ToHashSet();
        var relative = RelativePositions(s).ToLookup(p => p.SupplierId);
        return s.Suppliers.Select(sup =>
        {
            var invites = s.Invitations.Where(i => i.SupplierId == sup.Id && analyzed.Contains(i.RoundId)).ToList();
            var bids = s.Bids.Where(b => b.SupplierId == sup.Id).ToList();
            var participations = bids.Select(b => b.RoundId).Distinct().Count();
            var bidTenders = bids.Select(b => b.TenderId).ToHashSet();
            var awardedBid = bidTenders.Count(awardedTenders.Contains);
            var won = awards.Where(a => a.SupplierId == sup.Id).ToList();
            var positions = relative[sup.Id].ToList();
            var checkedBids = bids.Where(b => b.Compliance is ComplianceStatus.Compliant or ComplianceStatus.NonCompliant).ToList();
            var docs = s.Documents.Where(d => d.Owner == "Supplier" && d.SupplierId == sup.Id).ToList();
            var dates = bids.Select(b => b.AvailableAt).ToList();
            return new SupplierStats(sup.Id, sup.Name, sup.Category, sup.Status.ToString(), invites.Count, participations, bidTenders.Count, awardedBid, won.Count,
                awardedBid == 0 ? null : Math.Round((double)won.Count / awardedBid, 3),
                invites.Count == 0 ? null : Math.Round((double)bids.Count(b => invites.Any(i => i.Id == b.InvitationId)) / invites.Count, 3),
                won.GroupBy(a => a.Currency).Select(g => new CurrencyValue(g.Key, g.Sum(a => a.Value), g.Count())).ToList(),
                positions.Count == 0 ? null : Math.Round(positions.Average(p => p.Relative), 4), positions.Count,
                checkedBids.Count, checkedBids.Count(b => b.Compliance == ComplianceStatus.Compliant), docs.Count, docs.Count(d => d.Verification == DocumentVerificationStatus.Verified),
                dates.Count == 0 ? null : dates.Min(), dates.Count == 0 ? null : dates.Max());
        }).ToList();
    }

    public sealed record DnaDimension(string Key, string Label, double? Value, string Basis);
    public sealed record SupplierDna(int SupplierId, string Name, IReadOnlyList<DnaDimension> Dimensions);

    // Every dimension is a share between 0 and 1 with its numerator/denominator. Missing data stays null (never imputed).
    public static SupplierDna Dna(SupplierStats x, int maxParticipations) => new(x.SupplierId, x.Name,
    [
        new("participation", "Participation", maxParticipations == 0 ? null : (double)x.Participations / maxParticipations, $"{x.Participations} of the most active supplier's {maxParticipations} rounds"),
        new("response", "Response rate", x.ParticipationRate, x.Invitations == 0 ? "No closed-round invitations" : $"Offers in {Math.Round((x.ParticipationRate ?? 0) * x.Invitations)} of {x.Invitations} invited rounds"),
        new("winRate", "Win rate", x.WinRate, x.AwardedTendersBid == 0 ? "No awarded tender bid on" : $"{x.Wins} of {x.AwardedTendersBid} awarded tenders"),
        new("price", "Price position", x.RelativePrice is { } r ? Math.Clamp(0.5 - r * 2, 0, 1) : null, x.PricedRounds == 0 ? "No comparable rounds" : $"{(x.RelativePrice >= 0 ? "+" : "")}{x.RelativePrice:P1} vs round median over {x.PricedRounds} rounds (higher = cheaper)"),
        new("compliance", "Compliance", x.ComplianceChecked == 0 ? null : (double)x.Compliant / x.ComplianceChecked, x.ComplianceChecked == 0 ? "No compliance checks recorded" : $"{x.Compliant} of {x.ComplianceChecked} checked offers compliant"),
        new("documents", "Document verification", x.Documents == 0 ? null : (double)x.VerifiedDocuments / x.Documents, x.Documents == 0 ? "No registration documents" : $"{x.VerifiedDocuments} of {x.Documents} registration documents verified")
    ]);

    // ---------- Competition ----------

    public sealed record RoundCompetition(int RoundId, int TenderId, string Tender, int RoundNumber, DateTime Date, string Department, int Invited, int Submitted, int Compliant, bool LowCompetition);
    public sealed record GroupCompetition(string Group, int Rounds, int Bids, double AvgBids);
    public sealed record PairCount(int SupplierA, int SupplierB, string NameA, string NameB, int Rounds);
    public sealed record Competition(IReadOnlyList<RoundCompetition> Rounds, double? AvgBidsPerRound, double? AvgBidsPerTender, int LowCompetitionRounds,
        int MinimumBids, IReadOnlyList<GroupCompetition> Monthly, IReadOnlyList<GroupCompetition> ByDepartment, IReadOnlyList<PairCount> RecurringCompetitors);

    public static Competition CompetitionAnalysis(RadarSnapshot s, int minimumBids)
    {
        var rounds = s.AnalyzedRounds.OrderBy(r => r.ReferenceDate).Select(r =>
        {
            var tender = s.Tender(r.TenderId);
            var bids = s.Bids.Where(b => b.RoundId == r.Id).ToList();
            return new RoundCompetition(r.Id, r.TenderId, tender?.Title ?? $"Tender #{r.TenderId}", r.RoundNumber, r.ReferenceDate, tender?.Department ?? "-",
                s.Invitations.Count(i => i.RoundId == r.Id), bids.Count, bids.Count(b => b.Compliance == ComplianceStatus.Compliant), bids.Count < minimumBids);
        }).ToList();
        var tenders = rounds.Select(r => r.TenderId).Distinct().Count();
        return new Competition(rounds,
            rounds.Count == 0 ? null : Math.Round(rounds.Average(r => (double)r.Submitted), 2),
            tenders == 0 ? null : Math.Round((double)rounds.Sum(r => r.Submitted) / tenders, 2),
            rounds.Count(r => r.LowCompetition), minimumBids,
            rounds.GroupBy(r => r.Date.ToString("yyyy-MM")).OrderBy(g => g.Key).Select(g => new GroupCompetition(g.Key, g.Count(), g.Sum(r => r.Submitted), Math.Round(g.Average(r => (double)r.Submitted), 2))).ToList(),
            rounds.GroupBy(r => r.Department).OrderBy(g => g.Average(r => r.Submitted)).Select(g => new GroupCompetition(g.Key, g.Count(), g.Sum(r => r.Submitted), Math.Round(g.Average(r => (double)r.Submitted), 2))).ToList(),
            JointRounds(s).OrderByDescending(p => p.Value.Count).Take(10).Select(p => new PairCount(p.Key.A, p.Key.B, s.SupplierName(p.Key.A), s.SupplierName(p.Key.B), p.Value.Count)).ToList());
    }

    public static Dictionary<(int A, int B), List<int>> JointRounds(RadarSnapshot s)
    {
        var pairs = new Dictionary<(int, int), List<int>>();
        foreach (var round in s.Bids.GroupBy(b => b.RoundId))
        {
            var suppliers = round.Select(b => b.SupplierId).Distinct().Order().ToArray();
            for (var i = 0; i < suppliers.Length; i++)
                for (var j = i + 1; j < suppliers.Length; j++)
                {
                    var key = (suppliers[i], suppliers[j]);
                    if (!pairs.TryGetValue(key, out var list)) pairs[key] = list = [];
                    list.Add(round.Key);
                }
        }
        return pairs;
    }

    // ---------- Awards ----------

    public sealed record AwardShare(string Name, int? SupplierId, decimal Value, double Share, int Count);
    public sealed record AwardDistribution(string Currency, decimal Total, IReadOnlyList<AwardShare> BySupplier, IReadOnlyList<AwardShare> ByDepartment, double? Hhi);

    public static List<AwardDistribution> AwardDistributions(RadarSnapshot s) => Awards(s).GroupBy(a => a.Currency).Select(g =>
    {
        var total = g.Sum(a => a.Value);
        double Share(decimal v) => total == 0 ? 0 : (double)(v / total);
        var bySupplier = g.GroupBy(a => a.SupplierId).Select(x => new AwardShare(x.First().Supplier, x.Key, x.Sum(a => a.Value), Share(x.Sum(a => a.Value)), x.Count())).OrderByDescending(x => x.Value).ToList();
        return new AwardDistribution(g.Key, total, bySupplier,
            g.GroupBy(a => a.Department).Select(x => new AwardShare(x.Key, null, x.Sum(a => a.Value), Share(x.Sum(a => a.Value)), x.Count())).OrderByDescending(x => x.Value).ToList(),
            total == 0 ? null : Math.Round(bySupplier.Sum(x => x.Share * x.Share), 4));
    }).ToList();

    // ---------- Price intelligence ----------

    public sealed record RelativePosition(int SupplierId, int RoundId, int TenderId, DateTime Date, string Currency, decimal Total, decimal Median, double Relative, int Comparable);

    // Offer totals compared only inside one round (same tender items, quantities and revision boundary) and one currency.
    public static List<RelativePosition> RelativePositions(RadarSnapshot s)
    {
        var result = new List<RelativePosition>();
        foreach (var group in s.Bids.GroupBy(b => (b.RoundId, b.Currency)))
        {
            var bids = group.Where(b => b.Total > 0).ToList();
            if (bids.Count < 2) continue;
            var median = Median(bids.Select(b => b.Total));
            if (median <= 0) continue;
            var round = s.Round(group.Key.RoundId)!;
            result.AddRange(bids.Select(b => new RelativePosition(b.SupplierId, b.RoundId, b.TenderId, round.ReferenceDate, b.Currency, b.Total, median,
                Math.Round((double)(b.Total / median) - 1, 4), bids.Count)));
        }
        return result;
    }

    public sealed record ItemPrice(int SupplierId, string Supplier, int BidId, decimal UnitPrice);
    public sealed record ItemComparison(int TenderItemId, int TenderId, int RoundId, string Tender, string Item, string Unit, decimal Quantity, string Currency,
        IReadOnlyList<ItemPrice> Prices, decimal Min, decimal Median, decimal Max, double Spread);

    public static List<ItemComparison> ItemComparisons(RadarSnapshot s) => s.Bids
        .SelectMany(b => b.Lines.Select(l => (Bid: b, Line: l)))
        // Only lines that price the tender's own quantity and unit are comparable.
        .Where(x => x.Line.UnitPrice > 0 && x.Line.Quantity == x.Line.TenderQuantity)
        .GroupBy(x => (x.Bid.RoundId, x.Line.TenderItemId, x.Bid.Currency))
        .Where(g => g.Select(x => x.Bid.SupplierId).Distinct().Count() >= 2)
        .Select(g =>
        {
            var first = g.First();
            var prices = g.Select(x => new ItemPrice(x.Bid.SupplierId, s.SupplierName(x.Bid.SupplierId), x.Bid.Id, x.Line.UnitPrice)).OrderBy(p => p.UnitPrice).ToList();
            var min = prices.Min(p => p.UnitPrice); var max = prices.Max(p => p.UnitPrice);
            return new ItemComparison(first.Line.TenderItemId, first.Bid.TenderId, g.Key.RoundId, s.Tender(first.Bid.TenderId)?.Title ?? "-", first.Line.Item, first.Line.Unit,
                first.Line.TenderQuantity, g.Key.Currency, prices, min, Median(prices.Select(p => p.UnitPrice)), max, min == 0 ? 0 : Math.Round((double)((max - min) / min), 4));
        })
        .OrderBy(x => s.Round(x.RoundId)?.ReferenceDate).ToList();

    // ---------- Hypothetical dependency simulation ----------

    public sealed record SimRound(int RoundId, string Tender, int RoundNumber, int Before, int After, bool DropsBelowMinimum, bool LosesAllBidders, bool LosesAward);
    public sealed record Alternative(int SupplierId, string Name, string Category, int Participations, double? ParticipationRate);
    public sealed record Simulation(IReadOnlyList<int> Excluded, IReadOnlyList<string> ExcludedNames, int RoundsAffected, int RoundsBelowMinimumBefore, int RoundsBelowMinimumAfter,
        int RoundsWithoutBidders, double BidShareRemoved, double? HhiBefore, double? HhiAfter, IReadOnlyList<CurrencyValue> AwardExposure,
        IReadOnlyList<SimRound> Rounds, IReadOnlyList<Alternative> Alternatives, string Notice);

    public static Simulation Simulate(RadarSnapshot s, IReadOnlyCollection<int> excluded, int minimumBids)
    {
        var set = excluded.ToHashSet();
        var awards = Awards(s);
        var rounds = s.AnalyzedRounds.Select(r =>
        {
            var bidders = s.Bids.Where(b => b.RoundId == r.Id).Select(b => b.SupplierId).Distinct().ToList();
            var after = bidders.Count(id => !set.Contains(id));
            return new SimRound(r.Id, s.Tender(r.TenderId)?.Title ?? "-", r.RoundNumber, bidders.Count, after,
                bidders.Count >= minimumBids && after < minimumBids, bidders.Count > 0 && after == 0, awards.Any(a => a.RoundId == r.Id && set.Contains(a.SupplierId)));
        }).Where(r => r.Before > 0).ToList();
        double? Hhi(IEnumerable<RBid> bids)
        {
            var list = bids.ToList(); if (list.Count == 0) return null;
            return Math.Round(list.GroupBy(b => b.SupplierId).Sum(g => Math.Pow((double)g.Count() / list.Count, 2)), 4);
        }
        var excludedCategories = s.Suppliers.Where(x => set.Contains(x.Id)).Select(x => x.Category).ToHashSet();
        var stats = Suppliers(s).ToDictionary(x => x.SupplierId);
        var alternatives = s.Suppliers.Where(x => !set.Contains(x.Id) && x.Status == SupplierStatus.Approved && excludedCategories.Contains(x.Category))
            .Select(x => new Alternative(x.Id, x.Name, x.Category, stats[x.Id].Participations, stats[x.Id].ParticipationRate))
            .OrderByDescending(x => x.Participations).ThenBy(x => x.Name).Take(8).ToList();
        return new Simulation(set.Order().ToList(), set.Order().Select(s.SupplierName).ToList(), rounds.Count(r => r.After < r.Before),
            rounds.Count(r => r.Before < minimumBids), rounds.Count(r => r.After < minimumBids), rounds.Count(r => r.LosesAllBidders),
            s.Bids.Count == 0 ? 0 : Math.Round((double)s.Bids.Count(b => set.Contains(b.SupplierId)) / s.Bids.Count, 4),
            Hhi(s.Bids), Hhi(s.Bids.Where(b => !set.Contains(b.SupplierId))),
            awards.Where(a => set.Contains(a.SupplierId)).GroupBy(a => a.Currency).Select(g => new CurrencyValue(g.Key, g.Sum(a => a.Value), g.Count())).ToList(),
            rounds.Where(r => r.After < r.Before).ToList(), alternatives,
            "Hypothetical replay of recorded history only. It is not a forecast and changes no supplier status or procurement decision.");
    }

    public static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return 0;
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
