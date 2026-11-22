using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Radar;

// A read-only, in-memory copy of the procurement records Orange Radar may analyse for one period.
// Sealing rule: bids of a round become analysable only after bidding has closed (round Closed / Failed / SentToEvaluation,
// or its deadline has passed). Bids in still-open rounds are counted as "sealed" and never exposed, priced or sent to the AI.
public sealed record RadarPeriod(DateTime? From, DateTime? To)
{
    public static RadarPeriod All { get; } = new(null, null);
    public bool Contains(DateTime value) => (From == null || value >= From) && (To == null || value < To.Value.Date.AddDays(1));
    public string Label => From == null && To == null ? "All recorded history" : $"{From:yyyy-MM-dd} to {To:yyyy-MM-dd}";
}

public sealed record RSupplier(int Id, string Name, string Category, SupplierStatus Status, string Email, string? Phone, string? Address,
    string? TaxNumber, string RegistrationNumber, DateTime CreatedAt);
public sealed record RTender(int Id, string Title, TenderStatus Status, string Currency, decimal EstimatedValue, int DepartmentId, string Department,
    DateTime CreatedAt, int? AwardedBidId, DateTime? AwardedAt, string? Requirements, string Description);
public sealed record RRound(int Id, int TenderId, int RoundNumber, TenderRoundStatus Status, DateTime? Deadline, DateTime? ClosedAt, DateTime CreatedAt, bool Unsealed)
{
    // When the round's bids became visible to the organisation; the historical replay never shows a bid before this moment.
    public DateTime AvailableAt => ClosedAt ?? Deadline ?? CreatedAt;
    public DateTime ReferenceDate => Deadline ?? ClosedAt ?? CreatedAt;
}
public sealed record RInvitation(int Id, int RoundId, int TenderId, int SupplierId, InvitationStatus Status, DateTime InvitedAt);
public sealed record RBidLine(int TenderItemId, string Item, string Unit, decimal TenderQuantity, decimal Quantity, decimal UnitPrice,
    string? OfferedSpecifications = null);
public sealed record RBid(int Id, int InvitationId, int RoundId, int TenderId, int SupplierId, int Revision, string Currency, decimal Total,
    DateTime? SubmittedAt, ComplianceStatus Compliance, int? DeliveryDays, DateTime AvailableAt, IReadOnlyList<RBidLine> Lines,
    decimal? RecordedTotal = null, string? Deviations = null);
public sealed record RDocument(int Id, string FileName, string StoragePath, string Owner, int? BidId, int? SupplierId, int? TenderId,
    DateTime UploadedAt, DocumentVerificationStatus? Verification, string? DocumentType);

public sealed class RadarSnapshot
{
    public required DateTime GeneratedAt { get; init; }
    public required RadarPeriod Period { get; init; }
    public required IReadOnlyList<RSupplier> Suppliers { get; init; }
    public required IReadOnlyList<RTender> Tenders { get; init; }
    public required IReadOnlyList<RRound> Rounds { get; init; }
    public required IReadOnlyList<RInvitation> Invitations { get; init; }
    // Effective bids only: the latest submitted revision per invitation, in unsealed rounds of the period.
    public required IReadOnlyList<RBid> Bids { get; init; }
    public required IReadOnlyList<RDocument> Documents { get; init; }
    public int SealedRounds { get; init; }
    public int SealedBids { get; init; }
    // When the final decision notice reached the suppliers (FinalDecisionNotice.SuppliersNotifiedAt), per tender. Award outcomes only.
    public IReadOnlyDictionary<int, DateTime> WinnerNotifiedAt { get; init; } = new Dictionary<int, DateTime>();
    public RadarScope Scope { get; init; } = RadarScope.Market;

    private Dictionary<int, RSupplier>? supplierIndex;
    private Dictionary<int, RTender>? tenderIndex;
    private Dictionary<int, RRound>? roundIndex;
    public RSupplier? Supplier(int id) => (supplierIndex ??= Suppliers.ToDictionary(x => x.Id)).GetValueOrDefault(id);
    public RTender? Tender(int id) => (tenderIndex ??= Tenders.ToDictionary(x => x.Id)).GetValueOrDefault(id);
    public RRound? Round(int id) => (roundIndex ??= Rounds.ToDictionary(x => x.Id)).GetValueOrDefault(id);
    public string SupplierName(int id) => Supplier(id)?.Name ?? $"Supplier #{id}";
    public IEnumerable<RRound> AnalyzedRounds => Rounds.Where(r => r.Unsealed);
}

// What the signed-in user may analyse. Market = every tender (procurement, management, executives and Admin); otherwise only
// the tenders of the user's department and the tenders whose committee the user sits on, and only the suppliers invited to them.
public sealed record RadarScope(string Key, string Label, IReadOnlySet<int>? TenderIds)
{
    public static RadarScope Market { get; } = new("market", "Whole market", null);
    public bool IsMarket => TenderIds == null;

    public static readonly string[] MarketRoles = [SystemRoles.Admin, SystemRoles.SourcingOfficer, SystemRoles.SourcingManager];
    public static bool HasMarketView(ClaimsPrincipal user) =>
        MarketRoles.Any(user.IsInRole) || TopExecutiveRoles.Allowed.Split(',').Any(user.IsInRole);
}

public sealed class RadarDataService(ApplicationDbContext db, IMemoryCache cache, IOptions<RadarOptions> options, TimeProvider clock, IHttpContextAccessor? http = null)
{
    public DateTime Now => clock.GetUtcNow().UtcDateTime;
    private RadarScope? scope;

    // Resolved once per request from the signed-in user; no user (background jobs, checks) means the whole market.
    public async Task<RadarScope> ScopeAsync(CancellationToken ct)
    {
        if (scope != null) return scope;
        var user = http?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true || RadarScope.HasMarketView(user)) return scope = RadarScope.Market;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (user.IsInRole(SystemRoles.Supplier)) return scope = new RadarScope($"none:{userId}", "No access", new HashSet<int>());
        var department = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DepartmentId).FirstOrDefaultAsync(ct);
        var ids = await db.Tenders.AsNoTracking()
            .Where(t => (department != null && t.PurchaseRequest.DepartmentId == department) || t.CommitteeMembers.Any(m => m.UserId == userId))
            .Select(t => t.Id).ToListAsync(ct);
        return scope = new RadarScope($"user:{userId}", department != null ? "Your department and committee tenders" : "Your committee tenders", ids.ToHashSet());
    }

    public async Task<RadarSnapshot> LoadAsync(RadarPeriod period, CancellationToken ct)
    {
        var sc = await ScopeAsync(ct);
        var key = $"orange-radar:snapshot:{sc.Key}:{period.From:O}:{period.To:O}";
        if (cache.TryGetValue(key, out RadarSnapshot? cached) && cached != null) return cached;
        var snapshot = await BuildAsync(period, sc, ct);
        cache.Set(key, snapshot, TimeSpan.FromSeconds(Math.Clamp(options.Value.CacheSeconds, 0, 600)));
        return snapshot;
    }

    public void Invalidate(RadarPeriod period) => cache.Remove($"orange-radar:snapshot:{scope?.Key ?? "market"}:{period.From:O}:{period.To:O}");

    private async Task<RadarSnapshot> BuildAsync(RadarPeriod period, RadarScope sc, CancellationToken ct)
    {
        var now = Now;
        var allowedTenders = sc.TenderIds;
        var suppliers = await db.Suppliers.AsNoTracking()
            .Select(s => new RSupplier(s.Id, s.CompanyName, s.Category, s.Status, s.Email, s.Phone, s.Address, s.TaxNumber, s.RegistrationNumber, s.CreatedAt))
            .ToListAsync(ct);
        if (allowedTenders != null)
        {
            // Scoped users see only suppliers invited to one of their tenders, never the whole supplier register.
            var invited = (await db.TenderInvitations.AsNoTracking().Select(i => new { i.SupplierId, i.TenderRound.TenderId }).ToListAsync(ct))
                .Where(i => allowedTenders.Contains(i.TenderId)).Select(i => i.SupplierId).ToHashSet();
            suppliers = suppliers.Where(s => invited.Contains(s.Id)).ToList();
        }
        var supplierSet = suppliers.Select(s => s.Id).ToHashSet();
        var tenders = await db.Tenders.AsNoTracking()
            .Select(t => new RTender(t.Id, t.Title, t.Status, t.Currency, t.EstimatedValue, t.PurchaseRequest.DepartmentId, t.PurchaseRequest.Department.Name,
                t.CreatedAt, t.AwardedBidId, t.AwardedAt, t.Requirements, t.Description))
            .ToListAsync(ct);
        var roundRows = await db.TenderRounds.AsNoTracking()
            .Select(r => new { r.Id, r.TenderId, r.RoundNumber, r.Status, r.Deadline, r.ClosedAt, r.CreatedAt })
            .ToListAsync(ct);
        var allRounds = roundRows
            .Select(r => new RRound(r.Id, r.TenderId, r.RoundNumber, r.Status, r.Deadline, r.ClosedAt, r.CreatedAt, IsUnsealed(r.Status, r.Deadline, now)))
            .Where(r => period.Contains(r.ReferenceDate) && (allowedTenders == null || allowedTenders.Contains(r.TenderId)))
            .ToList();
        var roundIds = allRounds.Select(r => r.Id).ToHashSet();
        var unsealed = allRounds.Where(r => r.Unsealed).Select(r => r.Id).ToHashSet();

        var invitations = (await db.TenderInvitations.AsNoTracking()
                .Select(i => new { i.Id, i.TenderRoundId, i.TenderRound.TenderId, i.SupplierId, i.Status, i.InvitedAt })
                .ToListAsync(ct))
            .Where(i => roundIds.Contains(i.TenderRoundId))
            .Select(i => new RInvitation(i.Id, i.TenderRoundId, i.TenderId, i.SupplierId, i.Status, i.InvitedAt))
            .ToList();

        // Latest submitted revision per invitation. Superseded and withdrawn versions are never treated as offers.
        var submitted = await db.Bids.AsNoTracking()
            .Where(b => b.Status == BidStatus.Submitted)
            .Select(b => new
            {
                b.Id, b.TenderInvitationId, RoundId = b.TenderInvitation.TenderRoundId, b.TenderInvitation.TenderRound.TenderId, b.TenderInvitation.SupplierId,
                b.RevisionNumber, b.CurrencyCode, b.SubmittedAt, b.ComplianceStatus, b.DeliveryDays, b.Deviations,
                Extras = b.ShippingCost + b.InstallationCost + b.MaintenanceCost + b.TaxAmount + b.OtherCost
            })
            .ToListAsync(ct);
        var effective = submitted.Where(b => roundIds.Contains(b.RoundId))
            .GroupBy(b => b.TenderInvitationId).Select(g => g.OrderByDescending(b => b.RevisionNumber).First()).ToList();
        var sealedBids = effective.Count(b => !unsealed.Contains(b.RoundId));
        var visible = effective.Where(b => unsealed.Contains(b.RoundId)).ToList();
        var visibleIds = visible.Select(b => b.Id).ToList();
        var lines = (await db.BidItems.AsNoTracking()
                .Where(i => visibleIds.Contains(i.BidId))
                .Select(i => new { i.BidId, i.TenderItemId, i.TenderItem.Name, i.TenderItem.Unit, TenderQuantity = i.TenderItem.Quantity, i.Quantity, i.UnitPrice, i.OfferedSpecifications })
                .ToListAsync(ct))
            .GroupBy(i => i.BidId).ToDictionary(g => g.Key, g => (IReadOnlyList<RBidLine>)g.Select(i => new RBidLine(i.TenderItemId, i.Name, i.Unit, i.TenderQuantity, i.Quantity, i.UnitPrice, i.OfferedSpecifications)).ToList());
        var roundById = allRounds.ToDictionary(r => r.Id);
        var bids = visible.Select(b =>
        {
            var bidLines = lines.GetValueOrDefault(b.Id) ?? [];
            var total = bidLines.Sum(l => l.Quantity * l.UnitPrice) + b.Extras;
            var available = roundById[b.RoundId].AvailableAt;
            if (b.SubmittedAt > available) available = b.SubmittedAt.Value;
            return new RBid(b.Id, b.TenderInvitationId, b.RoundId, b.TenderId, b.SupplierId, b.RevisionNumber, b.CurrencyCode, decimal.Round(total, 2),
                b.SubmittedAt, b.ComplianceStatus, b.DeliveryDays, available, bidLines, bidLines.Count > 0 ? total : null, b.Deviations);
        }).ToList();

        // Document metadata only. Bid documents follow the same sealing rule as their bids.
        var visibleSet = visibleIds.ToHashSet();
        var tenderIds = allRounds.Select(r => r.TenderId).ToHashSet();
        var documents = (await db.Documents.AsNoTracking()
                .Where(d => d.BidId != null || d.SupplierId != null || d.TenderId != null)
                .Select(d => new { d.Id, d.FileName, d.StoragePath, d.BidId, d.SupplierId, d.TenderId, d.UploadedAt, d.VerificationStatus, d.DocumentType })
                .ToListAsync(ct))
            .Where(d => d.BidId != null ? visibleSet.Contains(d.BidId.Value) : d.TenderId == null ? d.SupplierId == null || supplierSet.Contains(d.SupplierId.Value) : tenderIds.Contains(d.TenderId.Value))
            .Select(d => new RDocument(d.Id, d.FileName, d.StoragePath, d.BidId != null ? "Bid" : d.SupplierId != null ? "Supplier" : "Tender",
                d.BidId, d.BidId != null ? visible.First(b => b.Id == d.BidId).SupplierId : d.SupplierId, d.BidId != null ? visible.First(b => b.Id == d.BidId).TenderId : d.TenderId,
                d.UploadedAt, d.VerificationStatus, d.DocumentType))
            .ToList();

        var notified = (await db.FinalDecisionNotices.AsNoTracking()
                .Where(n => n.Outcome == FinalDecisionOutcome.Award && n.SuppliersNotifiedAt != null)
                .Select(n => new { n.TenderId, At = n.SuppliersNotifiedAt!.Value }).ToListAsync(ct))
            .Where(n => tenderIds.Contains(n.TenderId)).GroupBy(n => n.TenderId).ToDictionary(g => g.Key, g => g.Max(n => n.At));

        return new RadarSnapshot
        {
            GeneratedAt = now, Period = period, Scope = sc, WinnerNotifiedAt = notified, Suppliers = suppliers, Tenders = tenders.Where(t => tenderIds.Contains(t.Id)).ToList(), Rounds = allRounds,
            Invitations = invitations, Bids = bids, Documents = documents, SealedRounds = allRounds.Count(r => !r.Unsealed && r.Status == TenderRoundStatus.Open), SealedBids = sealedBids
        };
    }

    public static bool IsUnsealed(TenderRoundStatus status, DateTime? deadline, DateTime now) =>
        status is TenderRoundStatus.Closed or TenderRoundStatus.Failed or TenderRoundStatus.SentToEvaluation
        || (status == TenderRoundStatus.Open && deadline is { } d && d <= now);
}
