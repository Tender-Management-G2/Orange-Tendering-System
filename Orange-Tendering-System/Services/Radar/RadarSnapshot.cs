using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
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
public sealed record RBidLine(int TenderItemId, string Item, string Unit, decimal TenderQuantity, decimal Quantity, decimal UnitPrice);
public sealed record RBid(int Id, int InvitationId, int RoundId, int TenderId, int SupplierId, int Revision, string Currency, decimal Total,
    DateTime? SubmittedAt, ComplianceStatus Compliance, int? DeliveryDays, DateTime AvailableAt, IReadOnlyList<RBidLine> Lines);
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

    private Dictionary<int, RSupplier>? supplierIndex;
    private Dictionary<int, RTender>? tenderIndex;
    private Dictionary<int, RRound>? roundIndex;
    public RSupplier? Supplier(int id) => (supplierIndex ??= Suppliers.ToDictionary(x => x.Id)).GetValueOrDefault(id);
    public RTender? Tender(int id) => (tenderIndex ??= Tenders.ToDictionary(x => x.Id)).GetValueOrDefault(id);
    public RRound? Round(int id) => (roundIndex ??= Rounds.ToDictionary(x => x.Id)).GetValueOrDefault(id);
    public string SupplierName(int id) => Supplier(id)?.Name ?? $"Supplier #{id}";
    public IEnumerable<RRound> AnalyzedRounds => Rounds.Where(r => r.Unsealed);
}

public sealed class RadarDataService(ApplicationDbContext db, IMemoryCache cache, IOptions<RadarOptions> options, TimeProvider clock)
{
    public DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<RadarSnapshot> LoadAsync(RadarPeriod period, CancellationToken ct)
    {
        var key = $"orange-radar:snapshot:{period.From:O}:{period.To:O}";
        if (cache.TryGetValue(key, out RadarSnapshot? cached) && cached != null) return cached;
        var snapshot = await BuildAsync(period, ct);
        cache.Set(key, snapshot, TimeSpan.FromSeconds(Math.Clamp(options.Value.CacheSeconds, 0, 600)));
        return snapshot;
    }

    public void Invalidate(RadarPeriod period) => cache.Remove($"orange-radar:snapshot:{period.From:O}:{period.To:O}");

    private async Task<RadarSnapshot> BuildAsync(RadarPeriod period, CancellationToken ct)
    {
        var now = Now;
        var suppliers = await db.Suppliers.AsNoTracking()
            .Select(s => new RSupplier(s.Id, s.CompanyName, s.Category, s.Status, s.Email, s.Phone, s.Address, s.TaxNumber, s.RegistrationNumber, s.CreatedAt))
            .ToListAsync(ct);
        var tenders = await db.Tenders.AsNoTracking()
            .Select(t => new RTender(t.Id, t.Title, t.Status, t.Currency, t.EstimatedValue, t.PurchaseRequest.DepartmentId, t.PurchaseRequest.Department.Name,
                t.CreatedAt, t.AwardedBidId, t.AwardedAt, t.Requirements, t.Description))
            .ToListAsync(ct);
        var roundRows = await db.TenderRounds.AsNoTracking()
            .Select(r => new { r.Id, r.TenderId, r.RoundNumber, r.Status, r.Deadline, r.ClosedAt, r.CreatedAt })
            .ToListAsync(ct);
        var allRounds = roundRows
            .Select(r => new RRound(r.Id, r.TenderId, r.RoundNumber, r.Status, r.Deadline, r.ClosedAt, r.CreatedAt, IsUnsealed(r.Status, r.Deadline, now)))
            .Where(r => period.Contains(r.ReferenceDate))
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
                b.RevisionNumber, b.CurrencyCode, b.SubmittedAt, b.ComplianceStatus, b.DeliveryDays,
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
                .Select(i => new { i.BidId, i.TenderItemId, i.TenderItem.Name, i.TenderItem.Unit, TenderQuantity = i.TenderItem.Quantity, i.Quantity, i.UnitPrice })
                .ToListAsync(ct))
            .GroupBy(i => i.BidId).ToDictionary(g => g.Key, g => (IReadOnlyList<RBidLine>)g.Select(i => new RBidLine(i.TenderItemId, i.Name, i.Unit, i.TenderQuantity, i.Quantity, i.UnitPrice)).ToList());
        var roundById = allRounds.ToDictionary(r => r.Id);
        var bids = visible.Select(b =>
        {
            var bidLines = lines.GetValueOrDefault(b.Id) ?? [];
            var total = bidLines.Sum(l => l.Quantity * l.UnitPrice) + b.Extras;
            var available = roundById[b.RoundId].AvailableAt;
            if (b.SubmittedAt > available) available = b.SubmittedAt.Value;
            return new RBid(b.Id, b.TenderInvitationId, b.RoundId, b.TenderId, b.SupplierId, b.RevisionNumber, b.CurrencyCode, decimal.Round(total, 2),
                b.SubmittedAt, b.ComplianceStatus, b.DeliveryDays, available, bidLines);
        }).ToList();

        // Document metadata only. Bid documents follow the same sealing rule as their bids.
        var visibleSet = visibleIds.ToHashSet();
        var tenderIds = allRounds.Select(r => r.TenderId).ToHashSet();
        var documents = (await db.Documents.AsNoTracking()
                .Where(d => d.BidId != null || d.SupplierId != null || d.TenderId != null)
                .Select(d => new { d.Id, d.FileName, d.StoragePath, d.BidId, d.SupplierId, d.TenderId, d.UploadedAt, d.VerificationStatus, d.DocumentType })
                .ToListAsync(ct))
            .Where(d => d.BidId != null ? visibleSet.Contains(d.BidId.Value) : d.TenderId == null || tenderIds.Contains(d.TenderId.Value))
            .Select(d => new RDocument(d.Id, d.FileName, d.StoragePath, d.BidId != null ? "Bid" : d.SupplierId != null ? "Supplier" : "Tender",
                d.BidId, d.BidId != null ? visible.First(b => b.Id == d.BidId).SupplierId : d.SupplierId, d.BidId != null ? visible.First(b => b.Id == d.BidId).TenderId : d.TenderId,
                d.UploadedAt, d.VerificationStatus, d.DocumentType))
            .ToList();

        return new RadarSnapshot
        {
            GeneratedAt = now, Period = period, Suppliers = suppliers, Tenders = tenders.Where(t => tenderIds.Contains(t.Id)).ToList(), Rounds = allRounds,
            Invitations = invitations, Bids = bids, Documents = documents, SealedRounds = allRounds.Count(r => !r.Unsealed && r.Status == TenderRoundStatus.Open), SealedBids = sealedBids
        };
    }

    public static bool IsUnsealed(TenderRoundStatus status, DateTime? deadline, DateTime now) =>
        status is TenderRoundStatus.Closed or TenderRoundStatus.Failed or TenderRoundStatus.SentToEvaluation
        || (status == TenderRoundStatus.Open && deadline is { } d && d <= now);
}
