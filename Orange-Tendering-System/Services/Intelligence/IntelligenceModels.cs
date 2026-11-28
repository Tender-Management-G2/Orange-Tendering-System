// ORANGE INTELLIGENCE - every data class of the module, in two parts:
//   Part A. The procurement data loaded from the database (IntelligenceService.LoadFromDatabaseAsync).
//           It stays on the server: the calculators read it, the browser never receives it.
//   Part B. The answers sent to the browser (DTOs). The controller returns them with Json(...), and ASP.NET Core
//           turns every property into a camelCase JSON field: "TenderCount" in C# becomes "tenderCount" in JavaScript.
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Intelligence;

// ######################################################################
// Part A. Loaded procurement data (server only)
// ######################################################################

// The date filter of the page. Empty or invalid dates mean "no limit"; a reversed range is swapped.
public class DateRange
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public static DateRange All
    {
        get { return new DateRange(); }
    }

    // Reads the page's "yyyy-MM-dd" date inputs.
    public static DateRange Parse(string? from, string? to)
    {
        DateRange range = new DateRange();
        range.From = ParseDate(from);
        range.To = ParseDate(to);
        if (range.From != null && range.To != null && range.From > range.To)
        {
            DateTime? earlier = range.To;
            range.To = range.From;
            range.From = earlier;
        }
        return range;
    }

    private static DateTime? ParseDate(string? value)
    {
        System.Globalization.DateTimeStyles styles = System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal;
        if (DateTime.TryParseExact(value, "yyyy-MM-dd", null, styles, out DateTime date))
        {
            return date.Date;
        }
        return null;
    }

    // The "To" day is included completely (until midnight).
    public bool Contains(DateTime value)
    {
        bool afterStart = From == null || value >= From;
        bool beforeEnd = To == null || value < To.Value.Date.AddDays(1);
        return afterStart && beforeEnd;
    }

    public string Label
    {
        get
        {
            if (From == null && To == null)
            {
                return "All recorded history";
            }
            return From?.ToString("yyyy-MM-dd") + " to " + To?.ToString("yyyy-MM-dd");
        }
    }
}

// What the signed-in user may analyse. TenderIds == null means the whole market.
public record DataScope(string Key, string Label, HashSet<int>? TenderIds)
{
    public static DataScope Market { get; } = new DataScope("market", "Whole market", null);
    public bool IsMarket => TenderIds == null;
}

// One row per table, holding only the columns the calculations need (a "projection" of the entity).
public record SupplierRecord(int Id, string Name, string Category, SupplierStatus Status);
public record TenderRecord(int Id, string Title, TenderStatus Status, string Currency, decimal EstimatedValue, int DepartmentId, string Department,
    DateTime CreatedAt, int? AwardedBidId, DateTime? AwardedAt);
public record RoundRecord(int Id, int TenderId, int RoundNumber, TenderRoundStatus Status, DateTime? Deadline, DateTime? ClosedAt, DateTime CreatedAt, bool Unsealed)
{
    // When the cycle's offers became visible to the organisation.
    public DateTime AvailableAt => ClosedAt ?? Deadline ?? CreatedAt;
    // The date used by the period filter.
    public DateTime ReferenceDate => Deadline ?? ClosedAt ?? CreatedAt;
}
public record InvitationRecord(int Id, int RoundId, int TenderId, int SupplierId, InvitationStatus Status, DateTime InvitedAt);
public record BidLineRecord(int TenderItemId, string Item, string Unit, decimal TenderQuantity, decimal Quantity, decimal UnitPrice,
    string? OfferedSpecifications = null);
public record BidRecord(int Id, int InvitationId, int RoundId, int TenderId, int SupplierId, int Revision, string Currency, decimal Total,
    DateTime? SubmittedAt, ComplianceStatus Compliance, int? DeliveryDays, DateTime AvailableAt, List<BidLineRecord> Lines,
    decimal? RecordedTotal = null, string? Deviations = null);
public record SupplierDocumentRecord(int SupplierId, DocumentVerificationStatus? Verification);

// Everything one user may analyse for one period. Built by IntelligenceService.LoadFromDatabaseAsync.
public class ProcurementSnapshot
{
    public DateTime GeneratedAt { get; set; }
    public DateRange Period { get; set; } = DateRange.All;
    public DataScope Scope { get; set; } = DataScope.Market;
    public List<SupplierRecord> Suppliers { get; set; } = new List<SupplierRecord>();
    public List<TenderRecord> Tenders { get; set; } = new List<TenderRecord>();
    public List<RoundRecord> Rounds { get; set; } = new List<RoundRecord>();
    public List<InvitationRecord> Invitations { get; set; } = new List<InvitationRecord>();
    // Effective offers only: the latest submitted revision per invitation, in unsealed cycles of the period.
    public List<BidRecord> Bids { get; set; } = new List<BidRecord>();
    public List<SupplierDocumentRecord> SupplierDocuments { get; set; } = new List<SupplierDocumentRecord>();
    // Offers of cycles still open for bidding: counted, never loaded with their prices.
    public int SealedBids { get; set; }
    // When the award decision reached the suppliers, per tender.
    public Dictionary<int, DateTime> WinnerNotifiedAt { get; set; } = new Dictionary<int, DateTime>();

    public SupplierRecord? Supplier(int id)
    {
        return Suppliers.FirstOrDefault(s => s.Id == id);
    }

    public TenderRecord? Tender(int id)
    {
        return Tenders.FirstOrDefault(t => t.Id == id);
    }

    public RoundRecord? Round(int id)
    {
        return Rounds.FirstOrDefault(r => r.Id == id);
    }

    public string SupplierName(int id)
    {
        SupplierRecord? supplier = Supplier(id);
        return supplier != null ? supplier.Name : "Supplier #" + id;
    }

    // The cycles whose offers may be analysed.
    public IEnumerable<RoundRecord> AnalyzedRounds
    {
        get { return Rounds.Where(r => r.Unsealed); }
    }
}

// ######################################################################
// Part B. Answers sent to the browser (DTOs)
// ######################################################################

// ======================================================================
// Shared by the three tabs
// ======================================================================

// Facts about the analysed data that every tab shows in the context bar (scope label, demo marker, status line).
public class PageContext
{
    public string Scope { get; set; } = "";
    public bool IsWholeMarket { get; set; }
    public string PeriodLabel { get; set; } = "";
    public bool IsDemo { get; set; }
    public int AnalyzedRounds { get; set; }
    public int TenderCount { get; set; }
}

// An amount of money in one currency. Different currencies are never added together.
public class MoneyValue
{
    public string Currency { get; set; } = "";
    public decimal Value { get; set; }
    public int Count { get; set; }
}

// One official award: the awarded bid of a tender.
public class AwardRecord
{
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = "";
    public int RoundId { get; set; }
    public int BidId { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string Department { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal Value { get; set; }
    public DateTime? AwardedAt { get; set; }
}

// Two suppliers that submitted offers in the same closed bidding cycles.
public class SupplierPair
{
    public int SupplierA { get; set; }
    public int SupplierB { get; set; }
    public string NameA { get; set; } = "";
    public string NameB { get; set; } = "";
    public int SharedRounds { get; set; }
}

// ======================================================================
// Tab 1: Overview
// ======================================================================

public class OverviewData
{
    public PageContext Context { get; set; } = new PageContext();
    public int TenderCount { get; set; }
    public int CompletedTenders { get; set; }
    public int InProgressTenders { get; set; }
    public int SupplierCount { get; set; }
    public int ParticipatingSuppliers { get; set; }
    public int SubmittedOffers { get; set; }
    public int SealedOffers { get; set; }
    public double? MedianLifecycleDays { get; set; }
    public List<LifecycleStep> Lifecycle { get; set; } = new List<LifecycleStep>();
    public List<AttentionItem> Attention { get; set; } = new List<AttentionItem>();
}

// One step of the lifecycle funnel: how many tenders reached it, and the median days from tender creation.
public class LifecycleStep
{
    public string Key { get; set; } = "";
    public int Reached { get; set; }
    public double Share { get; set; }
    public double? MedianDays { get; set; }
}

// One line of "Worth your attention". Kind is "low-competition", "in-progress" or "silent-suppliers".
public class AttentionItem
{
    public string Kind { get; set; } = "";
    public int Count { get; set; }
    public int MinimumOffers { get; set; }
    public int? TenderId { get; set; }
    public string TenderTitle { get; set; } = "";
    public string Status { get; set; } = "";
    public double Days { get; set; }
    public List<string> Examples { get; set; } = new List<string>();
}

// ======================================================================
// Tab 2: Suppliers
// ======================================================================

// The filters the user chose on the Suppliers tab (sent in the query string).
public class SupplierFilters
{
    public string Department { get; set; } = "";
    public string Category { get; set; } = "";
    public string Activity { get; set; } = "all";
    public string Search { get; set; } = "";
}

// Statistics of one supplier (SupplierCalculator.BuildStats). The AI Studio supplier facts use the same row.
public class SupplierRow
{
    public int SupplierId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int CategoryColor { get; set; } = -1;
    public int SupplierColor { get; set; } = -1;
    public string Status { get; set; } = "";
    public bool IsApproved { get; set; }
    public int Invitations { get; set; }
    public int Participations { get; set; }
    public int Tenders { get; set; }
    public int AwardedTendersBid { get; set; }
    public int Wins { get; set; }
    public double? WinRate { get; set; }
    public double? ParticipationRate { get; set; }
    public List<MoneyValue> AwardedValue { get; set; } = new List<MoneyValue>();
    public double? RelativePrice { get; set; }
    public int PricedRounds { get; set; }
    public int ComplianceChecked { get; set; }
    public int Compliant { get; set; }
    public int Documents { get; set; }
    public int VerifiedDocuments { get; set; }
    public DateTime? FirstActivity { get; set; }
    public DateTime? LastActivity { get; set; }
}

// One offer compared with the median offer of its bidding cycle and currency.
public class PricePosition
{
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public int RoundId { get; set; }
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = "";
    public DateTime Date { get; set; }
    public string Currency { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Median { get; set; }
    public double Relative { get; set; }
    public int Comparable { get; set; }
}

public class CategoryCount
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public int Color { get; set; } = -1;
    public bool IsOthers { get; set; }
}

public class MonthCount
{
    public string Month { get; set; } = "";
    public int Suppliers { get; set; }
}

// One supplier of the drop-down lists, the 3D Supplier Universe and the AI Studio supplier list.
public class SupplierOption
{
    public int SupplierId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Participations { get; set; }
    public int CategoryColor { get; set; } = -1;
}

public class LandscapeSeries
{
    public string Category { get; set; } = "";
    public int Color { get; set; } = -1;
    public List<SupplierRow> Points { get; set; } = new List<SupplierRow>();
}

public class PositionSeries
{
    public int SupplierId { get; set; }
    public string Name { get; set; } = "";
    public int SupplierColor { get; set; } = -1;
    public List<PricePosition> Points { get; set; } = new List<PricePosition>();
}

// Suppliers connected through shared closed bidding cycles (directly or through each other).
public class CompetitionGroup
{
    public List<string> Names { get; set; } = new List<string>();
}

public class SuppliersData
{
    public PageContext Context { get; set; } = new PageContext();
    public SupplierFilters Filters { get; set; } = new SupplierFilters();
    public List<string> Departments { get; set; } = new List<string>();
    public List<CategoryCount> Categories { get; set; } = new List<CategoryCount>();

    // KPI tiles (for the suppliers that pass the filters)
    public int RegisteredSuppliers { get; set; }
    public int ApprovedSuppliers { get; set; }
    public int ParticipatingSuppliers { get; set; }
    public int CategoryCount { get; set; }
    public string LargestCategory { get; set; } = "";
    public int LargestCategoryCount { get; set; }
    public int Invitations { get; set; }
    public int Offers { get; set; }
    public double? ResponseRate { get; set; }

    // Market snapshot
    public List<CategoryCount> CategoryChart { get; set; } = new List<CategoryCount>();
    public List<MonthCount> ParticipationTrend { get; set; } = new List<MonthCount>();

    // Supplier table
    public List<SupplierRow> Suppliers { get; set; } = new List<SupplierRow>();

    // Chart 1: participation vs win (or response) rate
    public bool UseWinRate { get; set; }
    public int NotPlotted { get; set; }
    public decimal MaxAwardedValue { get; set; }
    public List<LandscapeSeries> Landscape { get; set; } = new List<LandscapeSeries>();
    public string LeaderName { get; set; } = "";

    // Chart 2: price position over time
    public List<PositionSeries> Positions { get; set; } = new List<PositionSeries>();
    public List<PricePosition> PositionRows { get; set; } = new List<PricePosition>();
    public double? AverageRelative { get; set; }
    public double? RelativeSpread { get; set; }
    public string LowestAverageName { get; set; } = "";
    public double? LowestAverage { get; set; }
    public int ShownOffers { get; set; }

    // Market relationships
    public int CompetitionMinimum { get; set; }
    public int PairCount { get; set; }
    public List<SupplierPair> Pairs { get; set; } = new List<SupplierPair>();
    public List<CompetitionGroup> Groups { get; set; } = new List<CompetitionGroup>();
    public int UnconnectedSuppliers { get; set; }

    // Drop-down lists (all suppliers in scope, not only the filtered ones)
    public List<SupplierOption> AllSuppliers { get; set; } = new List<SupplierOption>();
    public List<SupplierOption> ScenarioSuppliers { get; set; } = new List<SupplierOption>();
    public int MinimumOffers { get; set; }
}

// Everything the supplier details panel shows for one supplier.
public class SupplierDetails
{
    public SupplierRow Supplier { get; set; } = new SupplierRow();
    public List<SupplierRound> Rounds { get; set; } = new List<SupplierRound>();
    public int MaxOffers { get; set; }
    public List<PricePosition> Prices { get; set; } = new List<PricePosition>();
    public List<SupplierPair> Competitors { get; set; } = new List<SupplierPair>();
}

// A closed bidding cycle in which the supplier submitted an offer.
public class SupplierRound
{
    public int RoundId { get; set; }
    public int TenderId { get; set; }
    public string Tender { get; set; } = "";
    public int RoundNumber { get; set; }
    public DateTime Date { get; set; }
    public string Department { get; set; } = "";
    public int Offers { get; set; }
    public bool Awarded { get; set; }
}

// ---------- supplier offer comparison ----------

public class ComparedOffer
{
    public int BidId { get; set; }
    public string Supplier { get; set; } = "";
    public string? Currency { get; set; }
    public decimal? Total { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public decimal? SecondsBeforeDeadline { get; set; }
    public string? RecordedTime { get; set; }
}

public class OfferComparison
{
    public int TenderId { get; set; }
    public string Title { get; set; } = "";
    public int RoundId { get; set; }
    public int RoundNumber { get; set; }
    public ComparedOffer A { get; set; } = new ComparedOffer();
    public ComparedOffer B { get; set; } = new ComparedOffer();
    public DateTimeOffset? Deadline { get; set; }
    public string? PriceIssue { get; set; }
    public decimal? Difference { get; set; }
    public decimal? RelativeDifference { get; set; }
    public string? LowerOffer { get; set; }
    public decimal? SecondsApart { get; set; }
    public string? FirstSubmission { get; set; }
}

// ---------- what-if scenario ----------

public class SimulateRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
    public int[]? SupplierIds { get; set; }
}

public class SimulatedRound
{
    public int RoundId { get; set; }
    public string Tender { get; set; } = "";
    public int RoundNumber { get; set; }
    public int Before { get; set; }
    public int After { get; set; }
    public bool DropsBelowMinimum { get; set; }
    public bool LosesAllBidders { get; set; }
    public bool LosesAward { get; set; }
}

public class SupplierAlternative
{
    public int SupplierId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Participations { get; set; }
    public double? ParticipationRate { get; set; }
}

public class SimulationResult
{
    public List<int> Excluded { get; set; } = new List<int>();
    public List<string> ExcludedNames { get; set; } = new List<string>();
    public int RoundsAffected { get; set; }
    public int RoundsBelowMinimumBefore { get; set; }
    public int RoundsBelowMinimumAfter { get; set; }
    public int RoundsWithoutBidders { get; set; }
    public double BidShareRemoved { get; set; }
    public double? HhiBefore { get; set; }
    public double? HhiAfter { get; set; }
    public List<MoneyValue> AwardExposure { get; set; } = new List<MoneyValue>();
    public List<SimulatedRound> Rounds { get; set; } = new List<SimulatedRound>();
    public List<SupplierAlternative> Alternatives { get; set; } = new List<SupplierAlternative>();
    public int MinimumOffers { get; set; }
    public string Notice { get; set; } = "";
}

// ======================================================================
// Tab 3: Tenders
// ======================================================================

public class TendersData
{
    public PageContext Context { get; set; } = new PageContext();
    public List<TenderListItem> Tenders { get; set; } = new List<TenderListItem>();
    public int? DefaultTenderId { get; set; }
    public int MinimumOffers { get; set; }
    public List<RoundCompetition> Competition { get; set; } = new List<RoundCompetition>();
    public List<DepartmentCompetition> Departments { get; set; } = new List<DepartmentCompetition>();
    public List<SupplierPair> RecurringPairs { get; set; } = new List<SupplierPair>();
    public List<AwardDistribution> Awards { get; set; } = new List<AwardDistribution>();
}

public class TenderListItem
{
    public int TenderId { get; set; }
    public string Title { get; set; } = "";
    public string Department { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string State { get; set; } = "";
    public string Status { get; set; } = "";
    public double? DaysToNotification { get; set; }
}

// One closed bidding cycle: how many suppliers were invited and how many submitted an offer.
public class RoundCompetition
{
    public int RoundId { get; set; }
    public int TenderId { get; set; }
    public string Tender { get; set; } = "";
    public int RoundNumber { get; set; }
    public DateTime Date { get; set; }
    public string Department { get; set; } = "";
    public int Invited { get; set; }
    public int Submitted { get; set; }
    public int Compliant { get; set; }
    public bool LowCompetition { get; set; }
    public List<string> Bidders { get; set; } = new List<string>();
}

public class DepartmentCompetition
{
    public string Department { get; set; } = "";
    public int Rounds { get; set; }
    public int Bids { get; set; }
    public double AverageBids { get; set; }
}

public class AwardShare
{
    public string Name { get; set; } = "";
    public int? SupplierId { get; set; }
    public int SupplierColor { get; set; } = -1;
    public decimal Value { get; set; }
    public double Share { get; set; }
    public int Count { get; set; }
}

// Official award value in ONE currency, split by supplier and by department.
public class AwardDistribution
{
    public string Currency { get; set; } = "";
    public decimal Total { get; set; }
    public int Count { get; set; }
    public double? Hhi { get; set; }
    public List<AwardShare> BySupplier { get; set; } = new List<AwardShare>();
    public List<AwardShare> ByDepartment { get; set; } = new List<AwardShare>();
}

// Everything the Tenders tab shows for one selected tender.
public class TenderDetails
{
    public int TenderId { get; set; }
    public string Title { get; set; } = "";
    public string Department { get; set; } = "";
    public string Status { get; set; } = "";
    public string State { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal EstimatedValue { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AwardedAt { get; set; }
    public DateTime? WinnerNotifiedAt { get; set; }
    public double? DaysToNotification { get; set; }
    public double? DaysToAward { get; set; }
    public double DaysSoFar { get; set; }
    public int? AwardedSupplierId { get; set; }
    public string? AwardedSupplier { get; set; }
    public decimal? AwardedValue { get; set; }
    public string? AwardCurrency { get; set; }
    public int TotalInvitations { get; set; }
    public List<TenderStage> Stages { get; set; } = new List<TenderStage>();
    public List<TenderRound> Rounds { get; set; } = new List<TenderRound>();
}

// One lifecycle milestone. At is null when the milestone is not recorded (it is never estimated).
public class TenderStage
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public DateTime? At { get; set; }
    public string Source { get; set; } = "";
    public double? DaysFromCreation { get; set; }
}

// One bidding cycle of the tender. Bids stay empty while the cycle is sealed (bidding still open).
public class TenderRound
{
    public int RoundId { get; set; }
    public int RoundNumber { get; set; }
    public string Status { get; set; } = "";
    public DateTime? Deadline { get; set; }
    public DateTime? ClosedAt { get; set; }
    public bool Analysed { get; set; }
    public int Invited { get; set; }
    public int Offers { get; set; }
    public List<TenderOffer> Bids { get; set; } = new List<TenderOffer>();
    public List<OfferGroup> Groups { get; set; } = new List<OfferGroup>();
}

public class TenderOffer
{
    public int BidId { get; set; }
    public int SupplierId { get; set; }
    public string Supplier { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal Total { get; set; }
    public int? ComparisonGroup { get; set; }
    public double? RelativeToMedian { get; set; }
    public bool Awarded { get; set; }
    public string Compliance { get; set; } = "";
    public int? DeliveryDays { get; set; }
    public string ComparisonNote { get; set; } = "";
}

// Offers of one bidding cycle that can be compared: same currency and the same recorded items, quantities and units.
public class OfferGroup
{
    public int GroupId { get; set; }
    public string Currency { get; set; } = "";
    public List<TenderOffer> Offers { get; set; } = new List<TenderOffer>();
    public decimal Median { get; set; }
    public TenderOffer Lowest { get; set; } = new TenderOffer();
    public TenderOffer Highest { get; set; } = new TenderOffer();
    public int MedianOwners { get; set; }
    public string MedianOwnerName { get; set; } = "";
}

// ---------- Excel / PDF export ----------

public class ExportRequest
{
    public string Format { get; set; } = "";
    public string ReportType { get; set; } = "";
    public int? TenderId { get; set; }
    public int? RoundId { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
}

public class ExportFile
{
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "";
    public string FileName { get; set; } = "";
    public List<string> Sheets { get; set; } = new List<string>();
}

// A request the export cannot fulfil (unknown report, tender outside the user's scope). Its message is shown to the user.
public class IntelligenceExportException : Exception
{
    public IntelligenceExportException(string message) : base(message)
    {
    }
}
