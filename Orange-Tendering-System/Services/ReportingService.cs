using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

public sealed record ReportKpi(string Label, string Value);
public sealed record AnalyticsReportHeader(string PreparedBy, IReadOnlyList<string> Roles)
{
    public const string CompanyName = "Orange Jordan";
    public const string CompanyAddress = "Abdali Boulevard, Amman, Jordan";
    public const string Website = "orange.jo";
    public const string WebsiteUrl = "https://orange.jo/en";
}
public sealed record ReportDefinition(string Key, string Action, string Title, string Summary, string DateBasis, string Icon, string DefaultSection)
{
    public string Description => $"{Summary} {DateBasis}";
}
public sealed record ReportRowLink(int Row, int Column, string Action, long Id);
public sealed record ReportTable(string Key, string Title, string[] Columns, List<string[]> Rows)
{
    public List<ReportRowLink> Links { get; } = [];
}
public sealed class ReportModel
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTime GeneratedAt { get; init; }
    public AnalyticsFilter Filter { get; init; } = new();
    public AnalyticsReportHeader? Header { get; set; }
    public List<ReportKpi> Kpis { get; } = [];
    public List<ReportTable> Tables { get; } = [];
    // Workbooks open on their first data sheet; the scope/filters summary follows the tables.
    public bool SummaryLast { get; init; }
}

public sealed partial class ReportingService(ApplicationDbContext db, TimeProvider clock, bool ownsContext = false) : IDisposable
{
    public void Dispose()
    {
        if (ownsContext) db.Dispose();
    }

    public static readonly ReportDefinition[] Catalog =
    [
        new("purchase-requests", "PurchaseRequests", "Purchase Request Reports",
            "Request status, departments and approval times.", "Dates use request creation.", "request", "requests"),
        new("tenders", "Tenders", "Tender Reports",
            "Bidding activity, awards and overdue evaluations.", "Dates use tender creation.", "tender", "tenders"),
        new("suppliers", "Suppliers", "Supplier Reports",
            "Supplier status, legal documents and participation.", "Dates use supplier registration.", "users", "supplier-performance"),
        new("evaluations", "Evaluations", "Bids & Evaluation Reports",
            "Offer prices, scoring, exclusions and unified results.", "Dates use tender creation.", "compare", "price-comparison"),
        new("governance", "Governance", "Approvals & Decision Reports",
            "Recommendations, votes, contracts and audit activity.", "Dates use tender creation for decisions and event time for audit entries.", "check", "recommendations"),
        new("management", "Management", "Top Committee Statistics",
            "Supplier transactions, awards and repeat awards.", "Dates use registration for supplier counts and award dates for transactions.", "chart", "supplier-awards")
    ];

    private static string N(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
    private static string D(DateTime? value) => value?.ToJordan("yyyy-MM-dd") ?? "—";
    private static string Time(DateTime? value) => value?.ToJordan("yyyy-MM-dd HH:mm") ?? "—";
    private static string K(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Pct(int numerator, int denominator) => denominator == 0 ? "—" :
        (numerator * 100.0 / denominator).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    public static string Words(string value) => System.Text.RegularExpressions.Regex.Replace(value, "(?<=[a-z])(?=[A-Z])", " ");
    private static string T(int id) => $"T-{id:0000}";
    private static string PR(int id) => $"PR-{id:0000}";
    private static int SearchId(string text)
    {
        if (text.StartsWith("PR-", StringComparison.OrdinalIgnoreCase)) text = text[3..];
        else if (text.StartsWith("T-", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
    }

    public Task<ReportModel> BuildAsync(string key, AnalyticsFilter filter, AnalyticsReadScope scope, CancellationToken ct) => key switch
    {
        "dashboard" => DashboardReport(filter, ct),
        "purchase-requests" => PurchaseRequests(filter, ct),
        "tenders" => Tenders(filter, ct),
        "suppliers" => Suppliers(filter, ct),
        "evaluations" => Evaluations(filter, scope, ct),
        "governance" => Governance(filter, scope, ct),
        "management" => Management(filter, ct),
        _ => throw new KeyNotFoundException(key)
    };

    private ReportModel Start(string key, AnalyticsFilter filter)
    {
        var report = Catalog.Single(x => x.Key == key);
        return new ReportModel { Key = key, Title = report.Title, Description = report.Description, Filter = filter, GeneratedAt = clock.GetUtcNow().UtcDateTime };
    }

    public async Task<AnalyticsFilterForm> FilterFormAsync(string action, AnalyticsFilter filter, CancellationToken ct)
    {
        var departments = await db.Departments.AsNoTracking().OrderBy(d => d.Name).Select(d => new AnalyticsOption(d.Id, d.Name)).ToListAsync(ct);
        var suppliers = await db.Suppliers.AsNoTracking().OrderBy(s => s.CompanyName).Select(s => new AnalyticsOption(s.Id, s.CompanyName)).ToListAsync(ct);
        return new(action, filter, departments, suppliers);
    }

    private IQueryable<Tender> TenderQuery(AnalyticsFilter f, bool search = true, bool dates = true)
    {
        var query = db.Tenders.AsNoTracking();
        if (dates && f.StartUtc is { } start) query = query.Where(t => t.CreatedAt >= start);
        if (dates && f.EndUtc is { } end) query = query.Where(t => t.CreatedAt < end);
        if (f.DepartmentId is { } department) query = query.Where(t => t.PurchaseRequest.DepartmentId == department);
        if (f.SupplierId is { } supplier) query = query.Where(t => t.Rounds.Any(r => r.Invitations.Any(i => i.SupplierId == supplier)));
        if (f.TenderStatus is { } status)
            query = query.Where(t => t.Status == status || status == TenderStatus.TopCommitteeReview && t.Status == TopCommitteeCompatibility.LegacyTopReview);
        if (f.TenderScope == "closed") query = query.Where(t => ClosedStatuses.Contains(t.Status) || t.Status == TenderStatus.Cancelled && t.ClosedAt != null);
        if (f.TenderScope == "closed-unawarded")
            query = query.Where(t => t.Status != TenderStatus.Draft && t.Status != TenderStatus.Bidding && t.Status != TenderStatus.Awarded);
        if (f.TenderScope == "no-offers")
            query = query.Where(t => t.Rounds.Any(r => FinishedRounds.Contains(r.Status)) &&
                !t.Rounds.Any(r => r.Invitations.Any(i => i.Bids.Any(b => b.SubmittedAt != null || b.Status == BidStatus.Submitted))));
        if (f.TenderScope == "evaluation-delays")
        {
            var delayedIds = DelayedSessions(clock.GetUtcNow().UtcDateTime).Select(s => s.TenderRound.TenderId);
            query = query.Where(t => delayedIds.Contains(t.Id));
        }
        if (search && f.Search?.Trim() is { Length: > 0 } term)
        {
            var id = SearchId(term);
            var requestCode = term.StartsWith("PR-", StringComparison.OrdinalIgnoreCase);
            query = query.Where(t => t.Title.Contains(term) || t.PurchaseRequest.Title.Contains(term) ||
                t.PurchaseRequest.Department.Name.Contains(term) ||
                t.Rounds.Any(r => r.Invitations.Any(i => i.Supplier.CompanyName.Contains(term) || i.Supplier.RegistrationNumber.Contains(term))) ||
                (id > 0 && (requestCode ? t.PurchaseRequestId == id : t.Id == id)));
        }
        return query;
    }

    private IQueryable<PurchaseRequest> RequestQuery(AnalyticsFilter f)
    {
        var query = db.PurchaseRequests.AsNoTracking();
        if (f.StartUtc is { } start) query = query.Where(r => r.CreatedAt >= start);
        if (f.EndUtc is { } end) query = query.Where(r => r.CreatedAt < end);
        if (f.DepartmentId is { } department) query = query.Where(r => r.DepartmentId == department);
        if (f.RequestStatus == PurchaseRequestStatus.Approved) query = query.Where(r => ApprovedStatuses.Contains(r.Status));
        else if (f.RequestStatus is { } status) query = query.Where(r => r.Status == status);
        var linked = TenderQuery(f, dates: false);
        if (f.SupplierId.HasValue || f.TenderStatus.HasValue || f.TenderScope != null)
            query = query.Where(r => linked.Any(t => t.PurchaseRequestId == r.Id));
        else if (f.Search?.Trim() is { Length: > 0 } term)
        {
            var id = SearchId(term);
            var requestCode = !term.StartsWith("T-", StringComparison.OrdinalIgnoreCase);
            query = query.Where(r => r.Title.Contains(term) || r.Department.Name.Contains(term) || id > 0 && requestCode && r.Id == id ||
                linked.Any(t => t.PurchaseRequestId == r.Id));
        }
        return query;
    }

    private IQueryable<Supplier> SupplierQuery(AnalyticsFilter f)
    {
        var query = db.Suppliers.AsNoTracking();
        if (f.StartUtc is { } start) query = query.Where(s => s.CreatedAt >= start);
        if (f.EndUtc is { } end) query = query.Where(s => s.CreatedAt < end);
        if (f.SupplierId is { } supplier) query = query.Where(s => s.Id == supplier);
        if (f.SupplierStatus is { } status) query = query.Where(s => s.Status == status);
        if (f.DepartmentId.HasValue || f.TenderStatus.HasValue || f.TenderScope != null)
        {
            var tenders = TenderQuery(f, search: false, dates: false);
            query = query.Where(s => s.Invitations.Any(i => tenders.Any(t => t.Id == i.TenderRound.TenderId)));
        }
        if (f.Search?.Trim() is { Length: > 0 } term)
            query = query.Where(s => s.CompanyName.Contains(term) || s.RegistrationNumber.Contains(term) ||
                s.Documents.Any(d => d.FileName.Contains(term) || d.DocumentType != null && d.DocumentType.Contains(term)));
        return query;
    }

    private static readonly PurchaseRequestStatus[] ApprovedStatuses =
        [PurchaseRequestStatus.Accepted, PurchaseRequestStatus.Approved, PurchaseRequestStatus.ConvertedToTender];
    private static readonly TenderStatus[] ClosedStatuses =
        [TenderStatus.Evaluation, TenderStatus.OfficerReview, TenderStatus.ManagerReview, TenderStatus.TopCommitteeReview,
            TopCommitteeCompatibility.LegacyTopReview, TenderStatus.Awarded, TenderStatus.Rejected];
    private static readonly TenderRoundStatus[] FinishedRounds =
        [TenderRoundStatus.Closed, TenderRoundStatus.Failed, TenderRoundStatus.SentToEvaluation];

    private IQueryable<EvaluationSession> DelayedSessions(DateTime now) => db.Set<EvaluationSession>().AsNoTracking()
        .Where(s => s.TenderRound.Tender.Status == TenderStatus.Evaluation && s.TenderRound.Status == TenderRoundStatus.SentToEvaluation &&
            (s.State == SessionState.Scheduled || s.State == SessionState.Incomplete) && s.EndsAt <= now &&
            s.Invitations.Count(i => i.SubmittedAt != null) < 4);

    private static PurchaseRequestStatus RequestGroup(PurchaseRequestStatus status) =>
        ApprovedStatuses.Contains(status) ? PurchaseRequestStatus.Approved : status;
    private static readonly PurchaseRequestStatus[] RequestGroups =
        [PurchaseRequestStatus.Draft, PurchaseRequestStatus.Submitted, PurchaseRequestStatus.UnderReview,
            PurchaseRequestStatus.Approved, PurchaseRequestStatus.Rejected, PurchaseRequestStatus.Returned];

    private async Task<ReportModel> PurchaseRequests(AnalyticsFilter filter, CancellationToken ct)
    {
        var m = Start("purchase-requests", filter);
        var query = RequestQuery(filter);
        var requests = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new { r.Id, r.Title, Department = r.Department.Name, r.Priority, r.Status, r.CreatedAt, r.SubmittedAt }).ToListAsync(ct);
        var reviews = await db.PurchaseRequestReviews.AsNoTracking().Where(r => query.Any(q => q.Id == r.PurchaseRequestId))
            .OrderBy(r => r.PurchaseRequestId).ThenBy(r => r.SubmissionNumber)
            .Select(r => new { r.PurchaseRequestId, r.SubmissionNumber, r.SubmittedAt, r.Decision, r.ReviewedAt, r.Comment }).ToListAsync(ct);
        var approvedDurations = reviews.Where(r => r.Decision == ReviewDecision.Accepted && r.ReviewedAt >= r.SubmittedAt)
            .GroupBy(r => r.PurchaseRequestId).Select(g => g.Last())
            .ToDictionary(r => r.PurchaseRequestId, r => (r.ReviewedAt!.Value - r.SubmittedAt).TotalHours);
        var counts = requests.ToLookup(r => RequestGroup(r.Status));
        m.Kpis.AddRange([new("Total requests", K(requests.Count)), new("Approved", K(counts[PurchaseRequestStatus.Approved].Count())),
            new("Average submission-to-approval time", approvedDurations.Count == 0 ? "—" : approvedDurations.Values.Average().ToString("0.#", CultureInfo.InvariantCulture) + " h")]);
        m.Tables.Add(new("status", "Status breakdown", ["Status", "Requests", "Share"], RequestGroups.Select(s =>
            new[] { Words(s.ToString()), K(counts[s].Count()), Pct(counts[s].Count(), requests.Count) }).ToList()));
        m.Tables.Add(new("departments", "By department", ["Department", "Requests", "Approved", "Returned / rejected"], requests.GroupBy(r => r.Department).OrderByDescending(g => g.Count()).Select(g =>
            new[] { g.Key, K(g.Count()), K(g.Count(r => ApprovedStatuses.Contains(r.Status))), K(g.Count(r => r.Status is PurchaseRequestStatus.Returned or PurchaseRequestStatus.Rejected)) }).ToList()));
        m.Tables.Add(new("requests", "All purchase requests", ["Request", "Title", "Department", "Priority", "Status", "Created", "Last submitted", "Approval time (hours)"], requests.Select(r =>
            new[] { PR(r.Id), r.Title, r.Department, r.Priority.ToString(), Words(r.Status.ToString()), D(r.CreatedAt), D(r.SubmittedAt),
                approvedDurations.TryGetValue(r.Id, out var hours) ? hours.ToString("0.#", CultureInfo.InvariantCulture) : "—" }).ToList()));
        m.Tables.Add(new("review-history", "Submission and review history", ["Request", "Submission", "Submitted (Jordan)", "Decision", "Reviewed (Jordan)", "Comment"], reviews.Select(r =>
            new[] { PR(r.PurchaseRequestId), K(r.SubmissionNumber), Time(r.SubmittedAt), r.Decision.ToString(), Time(r.ReviewedAt), r.Comment ?? "—" }).ToList()));
        var items = await db.PurchaseRequestItems.AsNoTracking().Where(i => query.Any(r => r.Id == i.PurchaseRequestId))
            .OrderBy(i => i.PurchaseRequestId).ThenBy(i => i.Id).Select(i => new { i.PurchaseRequestId, i.Name, i.Quantity, i.Unit, i.Specifications }).ToListAsync(ct);
        m.Tables.Add(new("request-items", "Request items", ["Request", "Item", "Quantity", "Unit", "Specifications"], items.Select(i =>
            new[] { PR(i.PurchaseRequestId), i.Name, i.Quantity.ToString("0.###", CultureInfo.InvariantCulture), i.Unit, i.Specifications ?? "—" }).ToList()));
        return m;
    }

    private async Task<ReportModel> Tenders(AnalyticsFilter filter, CancellationToken ct)
    {
        var m = Start("tenders", filter);
        var query = TenderQuery(filter);
        var tenders = await query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => new { t.Id, t.Title, Department = t.PurchaseRequest.Department.Name, t.Status, t.EstimatedValue, t.Currency, t.CreatedAt, t.AwardedAt, t.ClosedAt }).ToListAsync(ct);
        var rounds = await db.TenderRounds.AsNoTracking().Where(r => query.Any(t => t.Id == r.TenderId)).OrderBy(r => r.TenderId).ThenBy(r => r.RoundNumber)
            .Select(r => new { r.TenderId, r.RoundNumber, r.Status, r.Deadline, r.ClosedAt,
                Invited = r.Invitations.Count(i => filter.SupplierId == null || i.SupplierId == filter.SupplierId),
                Responded = r.Invitations.Count(i => (filter.SupplierId == null || i.SupplierId == filter.SupplierId) &&
                    i.Bids.Any(b => b.SubmittedAt != null || b.Status == BidStatus.Submitted)),
                AnyOffers = r.Invitations.Any(i => i.Bids.Any(b => b.SubmittedAt != null || b.Status == BidStatus.Submitted)) }).ToListAsync(ct);
        var byTender = rounds.ToLookup(r => r.TenderId);
        var zero = byTender.Where(g => g.Any(r => FinishedRounds.Contains(r.Status)) && g.All(r => !r.AnyOffers)).Select(g => g.Key).ToHashSet();
        var delayed = await DelayedSessions(m.GeneratedAt).Where(s => query.Any(t => t.Id == s.TenderRound.TenderId)).OrderBy(s => s.EndsAt)
            .Select(s => new { s.TenderRound.TenderId, s.TenderRound.Tender.Title, s.TenderRound.RoundNumber, s.EndsAt,
                Submitted = s.Invitations.Count(i => i.SubmittedAt != null), s.State }).ToListAsync(ct);
        m.Kpis.AddRange([new("Total tenders", K(tenders.Count)), new("Open for bidding", K(tenders.Count(t => t.Status == TenderStatus.Bidding))),
            new("Closed to bidding", K(tenders.Count(t => ClosedStatuses.Contains(t.Status) || t.Status == TenderStatus.Cancelled && t.ClosedAt != null))),
            new("Awarded", K(tenders.Count(t => t.Status == TenderStatus.Awarded))),
            new("Supplier participation", Pct(rounds.Sum(r => r.Responded), rounds.Sum(r => r.Invited))),
            new("Tenders with no offers", K(zero.Count)), new("Overdue evaluations", K(delayed.Select(s => s.TenderId).Distinct().Count()))]);
        m.Tables.Add(new("status", "Status breakdown", ["Status", "Tenders"], Enum.GetValues<TenderStatus>().Select(s =>
            new[] { Words(s.ToString()), K(tenders.Count(t => t.Status == s)) }).ToList()));
        m.Tables.Add(new("tenders", "All tenders", ["Tender", "Title", "Department", "Status", "Estimated value", "Invited", "Offers", "Created", "Awarded"], tenders.Select(t =>
            new[] { T(t.Id), t.Title, t.Department, Words(t.Status.ToString()), N(t.EstimatedValue) + " " + t.Currency,
                K(byTender[t.Id].Sum(r => r.Invited)), K(byTender[t.Id].Sum(r => r.Responded)), D(t.CreatedAt), D(t.AwardedAt) }).ToList()));
        m.Tables.Add(new("rounds", "Bidding rounds", ["Tender", "Round", "Status", "Deadline (Jordan)", "Closed (Jordan)", "Invited", "Offers"], rounds.Select(r =>
            new[] { T(r.TenderId), K(r.RoundNumber), Words(r.Status.ToString()), Time(r.Deadline), Time(r.ClosedAt), K(r.Invited), K(r.Responded) }).ToList()));
        m.Tables.Add(new("no-offers", "Tenders without any offers", ["Tender", "Title", "Status"], tenders.Where(t => zero.Contains(t.Id)).Select(t =>
            new[] { T(t.Id), t.Title, Words(t.Status.ToString()) }).ToList()));
        m.Tables.Add(new("evaluation-delays", "Evaluations past the scoring deadline", ["Tender", "Title", "Bidding round", "Deadline (Jordan)", "Scores submitted", "State"], delayed.Select(s =>
            new[] { T(s.TenderId), s.Title, K(s.RoundNumber), Time(s.EndsAt), K(s.Submitted) + "/4", Words(s.State.ToString()) }).ToList()));
        return m;
    }

    private async Task<ReportModel> Suppliers(AnalyticsFilter filter, CancellationToken ct)
    {
        var m = Start("suppliers", filter);
        var query = SupplierQuery(filter);
        var suppliers = await query.OrderBy(s => s.CompanyName).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.CompanyName, s.Category, s.Status, s.CreatedAt }).ToListAsync(ct);
        var documents = await db.Documents.AsNoTracking().Where(d => query.Any(s => s.Id == d.SupplierId))
            .OrderBy(d => d.ExpiresAt).ThenBy(d => d.Id).Select(d => new
                { Supplier = d.Supplier!.CompanyName, d.FileName, d.DocumentType, d.VerificationStatus, d.VerifiedAt, d.ExpiresAt }).ToListAsync(ct);
        var tenders = TenderQuery(filter, search: false, dates: false);
        var invites = (await db.TenderInvitations.AsNoTracking().Where(i => query.Any(s => s.Id == i.SupplierId) && tenders.Any(t => t.Id == i.TenderRound.TenderId))
            .Select(i => new { i.SupplierId, Offered = i.Bids.Any(b => b.SubmittedAt != null || b.Status == BidStatus.Submitted),
                Won = i.TenderRound.Tender.Status == TenderStatus.Awarded && i.Bids.Any(b => b.Id == i.TenderRound.Tender.AwardedBidId) })
            .ToListAsync(ct)).ToLookup(i => i.SupplierId);
        var today = m.GeneratedAt.ToJordan().Date;
        var soon = today.AddDays(60);
        var expiring = documents.Where(d => d.ExpiresAt?.ToJordan().Date <= soon).ToList();
        var expired = expiring.Count(d => d.ExpiresAt!.Value.ToJordan().Date < today);
        m.Kpis.AddRange([new("Registered suppliers", K(suppliers.Count)), new("Approved", K(suppliers.Count(s => s.Status == SupplierStatus.Approved))),
            new("Suspended", K(suppliers.Count(s => s.Status == SupplierStatus.Suspended))),
            new("Documents awaiting verification", K(documents.Count(d => d.VerificationStatus is null or DocumentVerificationStatus.Pending))),
            new("Expired documents", K(expired)), new("Documents expiring within 60 days", K(expiring.Count - expired))]);
        m.Tables.Add(new("status", "Status breakdown", ["Status", "Suppliers"], Enum.GetValues<SupplierStatus>().Select(status =>
            new[] { status.ToString(), K(suppliers.Count(s => s.Status == status)) }).ToList()));
        m.Tables.Add(new("supplier-performance", "Supplier performance", ["Supplier", "Category", "Status", "Registered", "Invitations", "Offers", "Participation", "Awards"], suppliers.Select(s =>
        {
            var invitations = invites[s.Id].ToList();
            var offers = invitations.Count(i => i.Offered);
            return new[] { s.CompanyName, s.Category, s.Status.ToString(), D(s.CreatedAt), K(invitations.Count), K(offers),
                Pct(offers, invitations.Count), K(invitations.Count(i => i.Won)) };
        }).ToList()));
        m.Tables.Add(new("verification", "Legal document verification", ["Supplier", "Document", "Type", "Verification", "Verified", "Expires"], documents.Select(d =>
            new[] { d.Supplier, d.FileName, d.DocumentType ?? "—", (d.VerificationStatus ?? DocumentVerificationStatus.Pending).ToString(), D(d.VerifiedAt), D(d.ExpiresAt) }).ToList()));
        m.Tables.Add(new("document-expiry", "Document expiry tracking", ["Supplier", "Document", "Type", "Expires", "State"], expiring.Select(d =>
            new[] { d.Supplier, d.FileName, d.DocumentType ?? "—", D(d.ExpiresAt), d.ExpiresAt!.Value.ToJordan().Date < today ? "Expired" : "Expiring within 60 days" }).ToList()));
        return m;
    }
}
