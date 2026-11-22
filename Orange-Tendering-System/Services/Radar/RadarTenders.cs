using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Radar;

// Tender-level read model for the Orange Intelligence "Tenders" tab: lifecycle timestamps from their own records, every
// analysed (closed) round with its offers, and the official award. Durations are computed only between recorded events;
// a missing event leaves the stage "not recorded" or the tender "in progress" - never estimated.
public sealed record LifecycleStage(string Key, string Label, DateTime? At, string Source);
public sealed record TenderOffer(int BidId, int SupplierId, string Supplier, string Currency, decimal Total, int? ComparisonGroup,
    double? RelativeToMedian, bool Awarded, string Compliance, int? DeliveryDays, string ComparisonNote);
public sealed record TenderRoundView(int RoundId, int RoundNumber, string Status, DateTime? Deadline, DateTime? ClosedAt, bool Analysed, int Invited, int Offers, IReadOnlyList<TenderOffer> Bids);
public sealed record TenderLifecycle(
    int TenderId, string Title, string Department, string Status, string Currency, decimal EstimatedValue,
    DateTime CreatedAt, DateTime? AwardedAt, DateTime? WinnerNotifiedAt, double? DaysToNotification, double? DaysToAward, string State,
    int? AwardedSupplierId, string? AwardedSupplier, decimal? AwardedValue, string? AwardCurrency,
    IReadOnlyList<LifecycleStage> Stages, IReadOnlyList<TenderRoundView> Rounds);

public static class RadarTenders
{
    public static IReadOnlyList<TenderLifecycle> Build(RadarSnapshot s)
    {
        var awards = RadarAnalytics.Awards(s).GroupBy(a => a.TenderId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AwardedAt).First());
        return s.Tenders.OrderByDescending(t => t.CreatedAt).Select(t =>
        {
            var rounds = s.Rounds.Where(r => r.TenderId == t.Id).OrderBy(r => r.RoundNumber).ToList();
            var award = awards.GetValueOrDefault(t.Id);
            var notified = s.WinnerNotifiedAt.TryGetValue(t.Id, out var n) ? n : (DateTime?)null;
            var firstInvite = s.Invitations.Where(i => i.TenderId == t.Id).Select(i => (DateTime?)i.InvitedAt).Min();
            var closed = rounds.Where(r => r.ClosedAt != null).Select(r => r.ClosedAt).Max();
            var stages = new List<LifecycleStage>
            {
                new("created", "Tender created", t.CreatedAt, "Tenders.CreatedAt"),
                new("round", "Initial Bidding cycle created", rounds.Where(r => r.RoundNumber == 1).Select(r => (DateTime?)r.CreatedAt).Min(), "TenderRounds.CreatedAt"),
                new("invited", "First invitation", firstInvite, "TenderInvitations.InvitedAt"),
                new("closed", "Bidding closed", closed, "TenderRounds.ClosedAt"),
                new("awarded", "Official award", t.AwardedAt, "Tenders.AwardedAt"),
                new("notified", "Winner notification sent", notified, "FinalDecisionNotices.SuppliersNotifiedAt")
            };
            var views = rounds.Select(r =>
            {
                var bids = r.Unsealed ? s.Bids.Where(b => b.RoundId == r.Id).ToList() : [];
                var comparableGroups = bids.Where(b => b.Total > 0 && b.Lines.Count > 0)
                    .GroupBy(b => (b.Currency, Structure: RadarBehavior.StructureKey(b)))
                    .Select((g, index) => new { Id = index + 1, Bids = g.ToList(), Median = Median(g.Select(b => b.Total)) }).ToList();
                var groupByBid = comparableGroups.SelectMany(g => g.Bids.Select(b => (b.Id, Group: g))).ToDictionary(x => x.Id, x => x.Group);
                var offers = bids.OrderBy(b => b.Total).Select(b =>
                {
                    var group = groupByBid.GetValueOrDefault(b.Id);
                    return new TenderOffer(b.Id, b.SupplierId, s.SupplierName(b.SupplierId), b.Currency, b.Total, group?.Id,
                        group is { Bids.Count: >= 2, Median: > 0 } ? (double)((b.Total - group.Median) / group.Median) : null,
                        award != null && award.RoundId == r.Id && award.SupplierId == b.SupplierId, b.Compliance.ToString(), b.DeliveryDays,
                        group == null ? "Missing item structure or non-positive total" : group.Bids.Count < 2 ? "No second comparable offer" : "Comparable within this bidding cycle and currency");
                }).ToList();
                // Sealed rounds report counts of invitations only - an offer count would itself leak bidding activity.
                return new TenderRoundView(r.Id, r.RoundNumber, r.Status.ToString(), r.Deadline, r.ClosedAt, r.Unsealed,
                    s.Invitations.Count(i => i.RoundId == r.Id), r.Unsealed ? offers.Count : 0, offers);
            }).ToList();
            double? Days(DateTime? end) => end == null ? null : Math.Round((end.Value - t.CreatedAt).TotalDays, 1);
            var state = notified != null ? "Completed" : t.Status is TenderStatus.Cancelled or TenderStatus.Rejected ? "Stopped" : t.AwardedAt != null ? "Awarded" : "In progress";
            return new TenderLifecycle(t.Id, t.Title, t.Department, t.Status.ToString(), t.Currency, t.EstimatedValue, t.CreatedAt, t.AwardedAt, notified,
                Days(notified), Days(t.AwardedAt), state, award?.SupplierId, award == null ? null : s.SupplierName(award.SupplierId), award?.Value, award?.Currency,
                stages, views);
        }).ToList();
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var v = values.Order().ToList();
        if (v.Count == 0) return 0;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}
