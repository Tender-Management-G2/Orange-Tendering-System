// ORANGE INTELLIGENCE - the one service behind the Overview, Suppliers and Tenders tabs (and the data of AI Studio).
// Read it top to bottom:
//   1. Fixed business rules (constants)
//   2. One public method per tab request: load the data, then call that tab's calculator
//   3. LoadSnapshotAsync: the user's data scope, the 60-second cache and the refresh button
//   4. LoadFromDatabaseAsync: every EF Core query of the module, with the sealing rule
//   5. Small rules shared by the three calculators (median, awards, shared cycles, names)
// The calculators (OverviewCalculator, SupplierCalculator, TenderCalculator) never touch the database:
// they receive the ProcurementSnapshot built here (classes in IntelligenceModels.cs).
using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Intelligence;

public class IntelligenceService
{
    // ======================================================================
    // 1. Fixed business rules
    // ======================================================================

    // A closed bidding cycle with fewer offers than this has "low competition".
    public const int MinimumOffers = 3;
    // Two suppliers are linked in a competition group when they met in at least this many closed bidding cycles.
    public const int MinimumSharedRounds = 2;
    // How long a loaded snapshot is reused for the same user and period (the refresh button skips it).
    private const int CacheSeconds = 60;

    private readonly ApplicationDbContext db;
    private readonly IMemoryCache cache;
    private readonly IHttpContextAccessor httpContextAccessor;
    private DataScope? scope; // the signed-in user's scope, found once per request

    public IntelligenceService(ApplicationDbContext db, IMemoryCache cache, IHttpContextAccessor httpContextAccessor)
    {
        this.db = db;
        this.cache = cache;
        this.httpContextAccessor = httpContextAccessor;
    }

    // ======================================================================
    // 2. One method per tab request (called by IntelligenceController)
    // ======================================================================

    public async Task<OverviewData> GetOverviewAsync(string? from, string? to, bool refresh, CancellationToken ct)
    {
        ProcurementSnapshot snapshot = await LoadSnapshotAsync(from, to, refresh, ct);
        return OverviewCalculator.Build(snapshot, MinimumOffers);
    }

    public async Task<SuppliersData> GetSuppliersAsync(string? from, string? to, SupplierFilters filters, bool refresh, CancellationToken ct)
    {
        ProcurementSnapshot snapshot = await LoadSnapshotAsync(from, to, refresh, ct);
        return SupplierCalculator.Build(snapshot, filters, MinimumSharedRounds, MinimumOffers);
    }

    // Returns null when the supplier is not in the user's authorized scope.
    public async Task<SupplierDetails?> GetSupplierDetailsAsync(int supplierId, string? from, string? to, CancellationToken ct)
    {
        ProcurementSnapshot snapshot = await LoadSnapshotAsync(from, to, false, ct);
        return SupplierCalculator.BuildDetails(snapshot, supplierId);
    }

    // Uses all recorded history (not the date filter), like the AI Studio supplier facts.
    // Returns null when either supplier is not in the user's authorized scope.
    public async Task<List<OfferComparison>?> CompareSuppliersAsync(int supplierA, int supplierB, CancellationToken ct)
    {
        ProcurementSnapshot snapshot = await LoadSnapshotAsync(null, null, false, ct);
        if (snapshot.Supplier(supplierA) == null || snapshot.Supplier(supplierB) == null)
        {
            return null;
        }
        return SupplierCalculator.CompareOffers(snapshot, supplierA, supplierB);
    }

    // Returns null when no supplier was chosen or a chosen supplier is not in the user's authorized scope.
    public async Task<SimulationResult?> SimulateAsync(SimulateRequest request, CancellationToken ct)
    {
        ProcurementSnapshot snapshot = await LoadSnapshotAsync(request.From, request.To, false, ct);

        // At most ten distinct suppliers, each of them in scope.
        List<int> supplierIds = new List<int>();
        foreach (int id in request.SupplierIds ?? Array.Empty<int>())
        {
            if (!supplierIds.Contains(id) && supplierIds.Count < 10)
            {
                supplierIds.Add(id);
            }
        }
        if (supplierIds.Count == 0)
        {
            return null;
        }
        foreach (int id in supplierIds)
        {
            if (snapshot.Supplier(id) == null)
            {
                return null;
            }
        }
        return SupplierCalculator.Simulate(snapshot, supplierIds, MinimumOffers);
    }

    public async Task<TendersData> GetTendersAsync(string? from, string? to, bool refresh, CancellationToken ct)
    {
        ProcurementSnapshot snapshot = await LoadSnapshotAsync(from, to, refresh, ct);
        return TenderCalculator.Build(snapshot, MinimumOffers);
    }

    // Returns null when the tender is not in the user's scope or has no bidding cycle in the period.
    public async Task<TenderDetails?> GetTenderDetailsAsync(int tenderId, string? from, string? to, CancellationToken ct)
    {
        ProcurementSnapshot snapshot = await LoadSnapshotAsync(from, to, false, ct);
        return TenderCalculator.BuildTender(snapshot, tenderId);
    }

    // ======================================================================
    // 3. Loading the authorized data (scope + cache)
    // ======================================================================

    // The procurement records of the period that this user may analyse. Also used by IntelligenceExportService
    // (refresh = true, so a file is never built from old data) and IntelligenceAiService (whole history).
    public async Task<ProcurementSnapshot> LoadSnapshotAsync(string? from, string? to, bool refresh, CancellationToken ct)
    {
        DateRange period = DateRange.Parse(from, to);
        DataScope userScope = await GetScopeAsync(ct);

        // The cache key contains the user's scope and the period, so one user's data is never shown to another scope.
        string cacheKey = "orange-intelligence:" + userScope.Key + ":" + period.From?.ToString("O") + ":" + period.To?.ToString("O");
        if (refresh)
        {
            cache.Remove(cacheKey);
        }
        else if (cache.TryGetValue(cacheKey, out ProcurementSnapshot? cached) && cached != null)
        {
            return cached;
        }

        ProcurementSnapshot snapshot = await LoadFromDatabaseAsync(period, userScope, ct);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(CacheSeconds));
        return snapshot;
    }

    // Procurement roles, Admin and the top executives see the whole market. Anyone else sees only the tenders of their
    // department and the tenders whose committee they sit on (and only the suppliers invited to them).
    // The Intelligence page itself is for Sourcing Officers (market view); the rule also protects any other caller.
    private async Task<DataScope> GetScopeAsync(CancellationToken ct)
    {
        if (scope != null)
        {
            return scope;
        }
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        // No signed-in user (tests, background work) = the whole market.
        if (user == null || user.Identity?.IsAuthenticated != true || HasMarketView(user))
        {
            scope = DataScope.Market;
            return scope;
        }

        string userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (user.IsInRole(SystemRoles.Supplier))
        {
            scope = new DataScope("none:" + userId, "No access", new HashSet<int>());
            return scope;
        }

        int? departmentId = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.DepartmentId)
            .FirstOrDefaultAsync(ct);
        List<int> tenderIds = await db.Tenders.AsNoTracking()
            .Where(t => (departmentId != null && t.PurchaseRequest.DepartmentId == departmentId) || t.CommitteeMembers.Any(m => m.UserId == userId))
            .Select(t => t.Id)
            .ToListAsync(ct);
        string label = departmentId != null ? "Your department and committee tenders" : "Your committee tenders";
        scope = new DataScope("user:" + userId, label, tenderIds.ToHashSet());
        return scope;
    }

    private static bool HasMarketView(ClaimsPrincipal user)
    {
        if (user.IsInRole(SystemRoles.Admin) || user.IsInRole(SystemRoles.SourcingOfficer) || user.IsInRole(SystemRoles.SourcingManager))
        {
            return true;
        }
        foreach (string role in TopExecutiveRoles.Allowed.Split(','))
        {
            if (user.IsInRole(role))
            {
                return true;
            }
        }
        return false;
    }

    // ======================================================================
    // 4. EF Core queries (read-only: AsNoTracking, projections with Select)
    // ======================================================================

    // Sealing rule: the offers of a bidding cycle may be analysed only after bidding has closed. Offers of a cycle that is
    // still open are counted as "sealed" and never loaded with their prices, so no chart, file or AI prompt can show them.
    private async Task<ProcurementSnapshot> LoadFromDatabaseAsync(DateRange period, DataScope userScope, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        HashSet<int>? allowedTenders = userScope.TenderIds;

        // ---- Suppliers (table Suppliers). Contact details and tax numbers are not loaded: nothing here needs them. ----
        List<SupplierRecord> suppliers = await db.Suppliers.AsNoTracking()
            .Select(s => new SupplierRecord(s.Id, s.CompanyName, s.Category, s.Status))
            .ToListAsync(ct);
        if (allowedTenders != null)
        {
            // A scoped user sees only the suppliers invited to one of their tenders, never the whole supplier register.
            var invitedRows = await db.TenderInvitations.AsNoTracking()
                .Select(i => new { i.SupplierId, i.TenderRound.TenderId })
                .ToListAsync(ct);
            HashSet<int> invitedSuppliers = invitedRows.Where(i => allowedTenders.Contains(i.TenderId)).Select(i => i.SupplierId).ToHashSet();
            suppliers = suppliers.Where(s => invitedSuppliers.Contains(s.Id)).ToList();
        }
        HashSet<int> supplierIds = suppliers.Select(s => s.Id).ToHashSet();

        // ---- Tenders (Tenders + PurchaseRequests + Departments) ----
        List<TenderRecord> allTenders = await db.Tenders.AsNoTracking()
            .Select(t => new TenderRecord(t.Id, t.Title, t.Status, t.Currency, t.EstimatedValue, t.PurchaseRequest.DepartmentId,
                t.PurchaseRequest.Department.Name, t.CreatedAt, t.AwardedBidId, t.AwardedAt))
            .ToListAsync(ct);

        // ---- Bidding cycles (TenderRounds): only those in the period and in the user's scope ----
        var roundRows = await db.TenderRounds.AsNoTracking()
            .Select(r => new { r.Id, r.TenderId, r.RoundNumber, r.Status, r.Deadline, r.ClosedAt, r.CreatedAt })
            .ToListAsync(ct);
        List<RoundRecord> rounds = new List<RoundRecord>();
        foreach (var row in roundRows)
        {
            RoundRecord round = new RoundRecord(row.Id, row.TenderId, row.RoundNumber, row.Status, row.Deadline, row.ClosedAt, row.CreatedAt,
                IsUnsealed(row.Status, row.Deadline, now));
            bool inPeriod = period.Contains(round.ReferenceDate);
            bool inScope = allowedTenders == null || allowedTenders.Contains(round.TenderId);
            if (inPeriod && inScope)
            {
                rounds.Add(round);
            }
        }
        HashSet<int> roundIds = rounds.Select(r => r.Id).ToHashSet();
        HashSet<int> unsealedRoundIds = rounds.Where(r => r.Unsealed).Select(r => r.Id).ToHashSet();
        HashSet<int> tenderIds = rounds.Select(r => r.TenderId).ToHashSet();

        // ---- Invitations (TenderInvitations) of those cycles ----
        var invitationRows = await db.TenderInvitations.AsNoTracking()
            .Select(i => new { i.Id, i.TenderRoundId, i.TenderRound.TenderId, i.SupplierId, i.Status, i.InvitedAt })
            .ToListAsync(ct);
        List<InvitationRecord> invitations = invitationRows
            .Where(i => roundIds.Contains(i.TenderRoundId))
            .Select(i => new InvitationRecord(i.Id, i.TenderRoundId, i.TenderId, i.SupplierId, i.Status, i.InvitedAt))
            .ToList();

        // ---- Offers (Bids): the latest submitted revision per invitation. Superseded and withdrawn versions are not offers. ----
        var submittedBids = await db.Bids.AsNoTracking()
            .Where(b => b.Status == BidStatus.Submitted)
            .Select(b => new
            {
                b.Id, b.TenderInvitationId, RoundId = b.TenderInvitation.TenderRoundId, b.TenderInvitation.TenderRound.TenderId,
                b.TenderInvitation.SupplierId, b.RevisionNumber, b.CurrencyCode, b.SubmittedAt, b.ComplianceStatus, b.DeliveryDays, b.Deviations,
                Extras = b.ShippingCost + b.InstallationCost + b.MaintenanceCost + b.TaxAmount + b.OtherCost
            })
            .ToListAsync(ct);
        var latestBids = submittedBids
            .Where(b => roundIds.Contains(b.RoundId))
            .GroupBy(b => b.TenderInvitationId)
            .Select(g => g.OrderByDescending(b => b.RevisionNumber).First())
            .ToList();
        int sealedBids = latestBids.Count(b => !unsealedRoundIds.Contains(b.RoundId));
        var visibleBids = latestBids.Where(b => unsealedRoundIds.Contains(b.RoundId)).ToList();
        List<int> visibleBidIds = visibleBids.Select(b => b.Id).ToList();

        // ---- Offer lines (BidItems + TenderItems): only for unsealed offers ----
        var lineRows = await db.BidItems.AsNoTracking()
            .Where(i => visibleBidIds.Contains(i.BidId))
            .Select(i => new { i.BidId, i.TenderItemId, i.TenderItem.Name, i.TenderItem.Unit, TenderQuantity = i.TenderItem.Quantity, i.Quantity, i.UnitPrice, i.OfferedSpecifications })
            .ToListAsync(ct);
        Dictionary<int, List<BidLineRecord>> linesByBid = new Dictionary<int, List<BidLineRecord>>();
        foreach (var line in lineRows)
        {
            if (!linesByBid.ContainsKey(line.BidId))
            {
                linesByBid[line.BidId] = new List<BidLineRecord>();
            }
            linesByBid[line.BidId].Add(new BidLineRecord(line.TenderItemId, line.Name, line.Unit, line.TenderQuantity, line.Quantity, line.UnitPrice, line.OfferedSpecifications));
        }

        Dictionary<int, RoundRecord> roundById = rounds.ToDictionary(r => r.Id);
        List<BidRecord> bids = new List<BidRecord>();
        foreach (var bid in visibleBids)
        {
            List<BidLineRecord> lines = linesByBid.ContainsKey(bid.Id) ? linesByBid[bid.Id] : new List<BidLineRecord>();
            // Offer total = sum of (quantity x unit price) + shipping, installation, maintenance, tax and other costs.
            decimal total = lines.Sum(l => l.Quantity * l.UnitPrice) + bid.Extras;
            // An offer is "visible" from the moment its cycle closed (or later, if it was submitted later).
            DateTime availableAt = roundById[bid.RoundId].AvailableAt;
            if (bid.SubmittedAt > availableAt)
            {
                availableAt = bid.SubmittedAt.Value;
            }
            decimal? recordedTotal = lines.Count > 0 ? total : null;
            bids.Add(new BidRecord(bid.Id, bid.TenderInvitationId, bid.RoundId, bid.TenderId, bid.SupplierId, bid.RevisionNumber, bid.CurrencyCode,
                decimal.Round(total, 2), bid.SubmittedAt, bid.ComplianceStatus, bid.DeliveryDays, availableAt, lines, recordedTotal, bid.Deviations));
        }

        // ---- Supplier documents (Documents): metadata only, for the "verified documents" statistic ----
        var documentRows = await db.Documents.AsNoTracking()
            .Where(d => d.BidId == null && d.SupplierId != null)
            .Select(d => new { SupplierId = d.SupplierId!.Value, d.TenderId, d.VerificationStatus })
            .ToListAsync(ct);
        List<SupplierDocumentRecord> supplierDocuments = documentRows
            .Where(d => d.TenderId == null ? supplierIds.Contains(d.SupplierId) : tenderIds.Contains(d.TenderId.Value))
            .Select(d => new SupplierDocumentRecord(d.SupplierId, d.VerificationStatus))
            .ToList();

        // ---- Winner notifications (FinalDecisionNotices): when the award decision reached the suppliers ----
        var notices = await db.FinalDecisionNotices.AsNoTracking()
            .Where(n => n.Outcome == FinalDecisionOutcome.Award && n.SuppliersNotifiedAt != null)
            .Select(n => new { n.TenderId, NotifiedAt = n.SuppliersNotifiedAt!.Value })
            .ToListAsync(ct);
        Dictionary<int, DateTime> winnerNotifiedAt = new Dictionary<int, DateTime>();
        foreach (var notice in notices)
        {
            if (!tenderIds.Contains(notice.TenderId))
            {
                continue;
            }
            if (!winnerNotifiedAt.ContainsKey(notice.TenderId) || notice.NotifiedAt > winnerNotifiedAt[notice.TenderId])
            {
                winnerNotifiedAt[notice.TenderId] = notice.NotifiedAt;
            }
        }

        ProcurementSnapshot snapshot = new ProcurementSnapshot();
        snapshot.GeneratedAt = now;
        snapshot.Period = period;
        snapshot.Scope = userScope;
        snapshot.Suppliers = suppliers;
        snapshot.Tenders = allTenders.Where(t => tenderIds.Contains(t.Id)).ToList();
        snapshot.Rounds = rounds;
        snapshot.Invitations = invitations;
        snapshot.Bids = bids;
        snapshot.SupplierDocuments = supplierDocuments;
        snapshot.SealedBids = sealedBids;
        snapshot.WinnerNotifiedAt = winnerNotifiedAt;
        return snapshot;
    }

    // A cycle is "unsealed" (its offers may be analysed) when it is Closed, Failed or SentToEvaluation,
    // or when it is Open but its deadline has passed. Draft cycles and open cycles before the deadline stay sealed.
    public static bool IsUnsealed(TenderRoundStatus status, DateTime? deadline, DateTime now)
    {
        if (status == TenderRoundStatus.Closed || status == TenderRoundStatus.Failed || status == TenderRoundStatus.SentToEvaluation)
        {
            return true;
        }
        return status == TenderRoundStatus.Open && deadline != null && deadline.Value <= now;
    }

    // ======================================================================
    // 5. Small rules shared by the three calculators
    // ======================================================================

    // The fictional demo dataset starts every name with "RADAR DEMO "; the page never shows that prefix.
    public static string CleanName(string? name)
    {
        string text = name ?? "";
        if (text.StartsWith("RADAR DEMO - "))
        {
            return text.Substring("RADAR DEMO - ".Length);
        }
        if (text.StartsWith("RADAR DEMO "))
        {
            return text.Substring("RADAR DEMO ".Length);
        }
        return text;
    }

    // "Initial Bidding" for cycle 1, "Re-tendering — Cycle 2" for later cycles.
    public static string CycleLabel(int roundNumber)
    {
        if (roundNumber == 1)
        {
            return "Initial Bidding";
        }
        return "Re-tendering — Cycle " + roundNumber;
    }

    // Median = the middle value after sorting (or the average of the two middle values). 0 when the list is empty.
    public static decimal Median(List<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        List<decimal> sorted = new List<decimal>(values);
        sorted.Sort();
        int middle = sorted.Count / 2;
        if (sorted.Count % 2 == 1)
        {
            return sorted[middle];
        }
        return (sorted[middle - 1] + sorted[middle]) / 2;
    }

    // Same rule for durations in days; null (not 0) when there is nothing to measure.
    public static double? Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }
        List<double> sorted = new List<double>(values);
        sorted.Sort();
        int middle = sorted.Count / 2;
        if (sorted.Count % 2 == 1)
        {
            return sorted[middle];
        }
        return (sorted[middle - 1] + sorted[middle]) / 2;
    }

    // Two offers are "structurally comparable" when they price the same tender items, units and quantities.
    // Example key: "11:UNIT:10:10|12:UNIT:10:10" (item id : unit : tender quantity : offered quantity).
    public static string StructureKey(BidRecord bid)
    {
        List<BidLineRecord> lines = bid.Lines.OrderBy(line => line.TenderItemId).ToList();
        List<string> parts = new List<string>();
        foreach (BidLineRecord line in lines)
        {
            string unit = line.Unit.Trim().ToUpperInvariant();
            string tenderQuantity = line.TenderQuantity.ToString(CultureInfo.InvariantCulture);
            string quantity = line.Quantity.ToString(CultureInfo.InvariantCulture);
            parts.Add(line.TenderItemId + ":" + unit + ":" + tenderQuantity + ":" + quantity);
        }
        return string.Join("|", parts);
    }

    // The context bar of every tab: scope label, period, demo marker and how many cycles were analysed.
    public static PageContext BuildContext(ProcurementSnapshot snapshot)
    {
        PageContext context = new PageContext();
        context.Scope = snapshot.Scope.Label;
        context.IsWholeMarket = snapshot.Scope.IsMarket;
        context.PeriodLabel = snapshot.Period.Label;
        context.TenderCount = snapshot.Tenders.Count;
        foreach (RoundRecord round in snapshot.Rounds)
        {
            if (round.Unsealed)
            {
                context.AnalyzedRounds++;
            }
        }
        // The "Demo dataset" marker: at least half of the suppliers belong to the fictional demo dataset.
        int demoSuppliers = 0;
        foreach (SupplierRecord supplier in snapshot.Suppliers)
        {
            if (supplier.Name.StartsWith("RADAR DEMO "))
            {
                demoSuppliers++;
            }
        }
        context.IsDemo = snapshot.Suppliers.Count > 0 && demoSuppliers >= snapshot.Suppliers.Count / 2.0;
        return context;
    }

    // Official awards: a tender whose AwardedBidId points to a visible (closed-cycle) offer. Oldest award first.
    public static List<AwardRecord> FindAwards(ProcurementSnapshot snapshot)
    {
        Dictionary<int, BidRecord> bidsById = new Dictionary<int, BidRecord>();
        foreach (BidRecord bid in snapshot.Bids)
        {
            bidsById[bid.Id] = bid;
        }

        List<AwardRecord> awards = new List<AwardRecord>();
        foreach (TenderRecord tender in snapshot.Tenders)
        {
            if (tender.AwardedBidId == null || !bidsById.ContainsKey(tender.AwardedBidId.Value))
            {
                continue;
            }
            BidRecord bid = bidsById[tender.AwardedBidId.Value];
            AwardRecord award = new AwardRecord();
            award.TenderId = tender.Id;
            award.TenderTitle = tender.Title;
            award.RoundId = bid.RoundId;
            award.BidId = bid.Id;
            award.SupplierId = bid.SupplierId;
            award.SupplierName = snapshot.SupplierName(bid.SupplierId);
            award.Department = tender.Department;
            award.Currency = bid.Currency;
            award.Value = bid.Total;
            award.AwardedAt = tender.AwardedAt;
            awards.Add(award);
        }
        return awards.OrderBy(award => award.AwardedAt).ToList();
    }

    // Supplier pairs that submitted offers in the same closed bidding cycle.
    // Key "3-7" = suppliers 3 and 7 (smaller id first); value = the ids of the shared cycles.
    public static Dictionary<string, List<int>> FindSharedRounds(ProcurementSnapshot snapshot)
    {
        // 1. Which suppliers submitted an offer in each closed cycle?
        Dictionary<int, List<int>> suppliersByRound = new Dictionary<int, List<int>>();
        List<int> roundOrder = new List<int>();
        foreach (BidRecord bid in snapshot.Bids)
        {
            if (!suppliersByRound.ContainsKey(bid.RoundId))
            {
                suppliersByRound[bid.RoundId] = new List<int>();
                roundOrder.Add(bid.RoundId);
            }
            if (!suppliersByRound[bid.RoundId].Contains(bid.SupplierId))
            {
                suppliersByRound[bid.RoundId].Add(bid.SupplierId);
            }
        }

        // 2. Every pair of suppliers inside one cycle shares that cycle.
        Dictionary<string, List<int>> pairs = new Dictionary<string, List<int>>();
        foreach (int roundId in roundOrder)
        {
            List<int> suppliers = suppliersByRound[roundId];
            suppliers.Sort();
            for (int i = 0; i < suppliers.Count; i++)
            {
                for (int j = i + 1; j < suppliers.Count; j++)
                {
                    string key = suppliers[i] + "-" + suppliers[j];
                    if (!pairs.ContainsKey(key))
                    {
                        pairs[key] = new List<int>();
                    }
                    pairs[key].Add(roundId);
                }
            }
        }
        return pairs;
    }

    // "3-7" -> 3 and 7
    public static int FirstOfPair(string key)
    {
        return int.Parse(key.Split('-')[0]);
    }

    public static int SecondOfPair(string key)
    {
        return int.Parse(key.Split('-')[1]);
    }
}
