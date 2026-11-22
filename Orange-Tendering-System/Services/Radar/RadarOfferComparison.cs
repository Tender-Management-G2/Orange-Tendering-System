using System.Globalization;

namespace Orange_Tendering_System.Services.Radar;

public sealed record ComparedOffer(int BidId, string Supplier, string? Currency, decimal? Total,
    DateTimeOffset? SubmittedAt, decimal? SecondsBeforeDeadline, string? RecordedTime);

public sealed record TenderOfferComparison(int TenderId, string Title, int RoundId, int RoundNumber,
    ComparedOffer A, ComparedOffer B, DateTimeOffset? Deadline, string? PriceIssue, decimal? Difference,
    decimal? RelativeDifference, string? LowerOffer, decimal? SecondsApart, string? FirstSubmission);

public static class RadarOfferComparison
{
    public const string BusinessZone = "Asia/Amman";
    private static readonly TimeZoneInfo Amman = TimeZoneInfo.FindSystemTimeZoneById(BusinessZone);

    public static IReadOnlyList<TenderOfferComparison> Build(RadarSnapshot snapshot, int supplierA, int supplierB)
    {
        if (supplierA == supplierB || snapshot.Supplier(supplierA) == null || snapshot.Supplier(supplierB) == null)
            return [];

        var pairs = from round in snapshot.AnalyzedRounds
                    where snapshot.Tender(round.TenderId) != null
                    join a in snapshot.Bids.Where(b => b.SupplierId == supplierA) on (round.Id, round.TenderId) equals (a.RoundId, a.TenderId)
                    join b in snapshot.Bids.Where(b => b.SupplierId == supplierB) on (round.Id, round.TenderId) equals (b.RoundId, b.TenderId)
                    select Compare(round, snapshot.Tender(round.TenderId)!, a, b, snapshot);

        return pairs.GroupBy(x => x.TenderId)
            .Select(g => g.OrderBy(x => x.PriceIssue != null).ThenByDescending(x => x.RoundNumber).First())
            .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.TenderId).ToList();
    }

    private static TenderOfferComparison Compare(RRound round, RTender tender, RBid a, RBid b, RadarSnapshot snapshot)
    {
        var issue = PriceIssue(a, b);
        var difference = issue == null ? Math.Abs(a.RecordedTotal!.Value - b.RecordedTotal!.Value) : (decimal?)null;
        var apart = SecondsBetween(a.SubmittedAt, b.SubmittedAt);
        ComparedOffer Offer(RBid bid)
        {
            var submitted = InAmman(bid.SubmittedAt);
            return new(bid.Id, snapshot.SupplierName(bid.SupplierId), Currency(bid), bid.RecordedTotal,
                submitted, SecondsBetween(bid.SubmittedAt, round.Deadline), RecordedTime(submitted));
        }

        return new(tender.Id, tender.Title, round.Id, round.RoundNumber, Offer(a), Offer(b), InAmman(round.Deadline),
            issue, difference, difference == null ? null : difference / Math.Max(a.RecordedTotal!.Value, b.RecordedTotal!.Value),
            difference is null or 0 ? null : a.RecordedTotal < b.RecordedTotal ? "a" : "b",
            apart == null ? null : Math.Abs(apart.Value), apart is null or 0 ? null : apart > 0 ? "a" : "b");
    }

    private static string? Currency(RBid bid) => string.IsNullOrWhiteSpace(bid.Currency) ? null : bid.Currency.Trim().ToUpperInvariant();

    private static string? PriceIssue(RBid a, RBid b)
    {
        if (Currency(a) == null || Currency(b) == null) return "missing_currency";
        if (Currency(a) != Currency(b)) return "different_currency";
        if (a.Lines.Count == 0 || b.Lines.Count == 0 || a.RecordedTotal is null or <= 0 || b.RecordedTotal is null or <= 0)
            return "missing_price";
        if (a.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || string.IsNullOrWhiteSpace(l.Unit)) ||
            b.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || string.IsNullOrWhiteSpace(l.Unit)) ||
            RadarBehavior.StructureKey(a) != RadarBehavior.StructureKey(b))
            return "different_structure";
        if (!a.Lines.OrderBy(l => l.TenderItemId).Select(l => Normalize(l.OfferedSpecifications))
            .SequenceEqual(b.Lines.OrderBy(l => l.TenderItemId).Select(l => Normalize(l.OfferedSpecifications))) ||
            Normalize(a.Deviations) != Normalize(b.Deviations))
            return "different_specifications";
        return null;
    }

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static decimal? SecondsBetween(DateTime? from, DateTime? to) =>
        from.HasValue && to.HasValue ? (to.Value - from.Value).Ticks / (decimal)TimeSpan.TicksPerSecond : null;

    private static DateTimeOffset? InAmman(DateTime? utc) =>
        utc.HasValue ? TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc)), Amman) : null;

    private static string? RecordedTime(DateTimeOffset? value) =>
        value?.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);

    public static string CycleLabel(int number, bool arabic = false) => number == 1
        ? arabic ? "العطاء الأولي" : "Initial Bidding"
        : arabic ? $"إعادة الطرح — الدورة {number}" : $"Re-tendering — Cycle {number}";

    public static string PriceReason(string issue, bool arabic) => (issue, arabic) switch
    {
        ("missing_currency", false) => "A currency is missing.",
        ("missing_currency", true) => "عملة أحد العرضين غير مسجلة.",
        ("different_currency", false) => "The offers use different currencies.",
        ("different_currency", true) => "العرضان بعملتين مختلفتين.",
        ("different_structure", false) => "The recorded items, quantities or units differ or are incomplete.",
        ("different_structure", true) => "البنود أو الكميات أو الوحدات المسجلة مختلفة أو غير مكتملة.",
        ("different_specifications", false) => "The recorded offered specifications or deviations differ.",
        ("different_specifications", true) => "المواصفات أو الانحرافات المسجلة في العرضين مختلفة.",
        (_, false) => "Both offers need recorded items and positive totals.",
        (_, true) => "تتطلب المقارنة بنوداً مسجلة وإجماليات موجبة لكلا العرضين."
    };
}
