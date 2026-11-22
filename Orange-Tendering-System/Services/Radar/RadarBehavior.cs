using System.Globalization;

namespace Orange_Tendering_System.Services.Radar;

// Pair observations use only the authorized, unsealed, effective bids in RadarSnapshot.
// A price gap is calculated only when both offers have the same currency and the same
// recorded tender items, quantities and units in one closed round.
public sealed record BehaviorObservation(
    int SupplierA, int SupplierB, int TenderId, string Tender, int RoundId, int RoundNumber,
    DateTime AvailableAt, DateTime? Deadline, int BidA, int BidB,
    DateTime? SubmittedA, DateTime? SubmittedB, double? MinutesApart,
    double? MinutesBeforeDeadlineA, double? MinutesBeforeDeadlineB,
    string? Currency, decimal? OfferA, decimal? OfferB, double? RelativeGap,
    bool PriceComparable, string PriceNote);

public static class RadarBehavior
{
    public static IReadOnlyList<BehaviorObservation> Build(RadarSnapshot snapshot)
    {
        var result = new List<BehaviorObservation>();
        foreach (var round in snapshot.AnalyzedRounds.OrderBy(r => r.AvailableAt).ThenBy(r => r.Id))
        {
            var bids = snapshot.Bids.Where(b => b.RoundId == round.Id).OrderBy(b => b.SupplierId).ToArray();
            var tender = snapshot.Tender(round.TenderId);
            for (var i = 0; i < bids.Length; i++)
            for (var j = i + 1; j < bids.Length; j++)
            {
                var a = bids[i]; var b = bids[j];
                var sameCurrency = string.Equals(a.Currency, b.Currency, StringComparison.OrdinalIgnoreCase);
                var recordedLines = a.Lines.Count > 0 && b.Lines.Count > 0;
                var sameStructure = recordedLines && StructureKey(a) == StructureKey(b);
                var valid = CompatiblePrice(a, b);
                var note = !sameCurrency ? "Different currencies" : !recordedLines ? "Item structure is not recorded for both offers"
                    : !sameStructure ? "Different tender items, quantities or units" : a.Total <= 0 || b.Total <= 0 ? "A total is missing or non-positive" : "Same closed round, currency, items, quantities and units";
                var minutes = a.SubmittedAt != null && b.SubmittedAt != null ? Math.Abs((a.SubmittedAt.Value - b.SubmittedAt.Value).TotalMinutes) : (double?)null;
                result.Add(new BehaviorObservation(a.SupplierId, b.SupplierId, round.TenderId, tender?.Title ?? $"Tender #{round.TenderId}", round.Id, round.RoundNumber,
                    round.AvailableAt, round.Deadline, a.Id, b.Id, a.SubmittedAt, b.SubmittedAt, minutes,
                    a.SubmittedAt != null && round.Deadline != null ? (round.Deadline.Value - a.SubmittedAt.Value).TotalMinutes : null,
                    b.SubmittedAt != null && round.Deadline != null ? (round.Deadline.Value - b.SubmittedAt.Value).TotalMinutes : null,
                    sameCurrency ? a.Currency : null, sameCurrency ? a.Total : null, sameCurrency ? b.Total : null,
                    valid ? (double)((b.Total - a.Total) / a.Total) : null, valid, note));
            }
        }
        return result;
    }

    public static string StructureKey(RBid bid) => string.Join("|", bid.Lines.OrderBy(l => l.TenderItemId).Select(l =>
        $"{l.TenderItemId}:{l.Unit.Trim().ToUpperInvariant()}:{l.TenderQuantity.ToString(CultureInfo.InvariantCulture)}:{l.Quantity.ToString(CultureInfo.InvariantCulture)}"));

    public static bool CompatiblePrice(RBid a, RBid b) => a.Total > 0 && b.Total > 0 &&
        string.Equals(a.Currency, b.Currency, StringComparison.OrdinalIgnoreCase) &&
        a.Lines.Count > 0 && b.Lines.Count > 0 && StructureKey(a) == StructureKey(b);
}
