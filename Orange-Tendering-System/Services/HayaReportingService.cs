using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

public sealed record HayaReportKpi(string Label, string Value);
public sealed record HayaReportTable(string Title, string[] Columns, List<string[]> Rows, int TotalRows = 0, int Page = 1, int PageSize = 0);
public sealed record HayaReportChart(string Title, List<HayaReportChartPoint> Points);
public sealed record HayaReportChartPoint(string Label, decimal Value);
public sealed class HayaReportFilters
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? DepartmentId { get; set; }
    public int? SupplierId { get; set; }
    public int? TenderId { get; set; }
    public PurchaseRequestStatus? RequestStatus { get; set; }
    public RequestPriority? Priority { get; set; }
    public TenderStatus? TenderStatus { get; set; }
    public ReportType? ReportType { get; set; }
    public ReportStatus? ReportStatus { get; set; }
    public int? ReportVersion { get; set; }
    public string? CreatedByUserId { get; set; }
    public DocumentVerificationStatus? VerificationStatus { get; set; }
    public string? Search { get; set; }
    public int ExpiryDays { get; set; } = 60;
    public int ExpiryPage { get; set; } = 1;
    public int PerformancePage { get; set; } = 1;
    public int ReportPage { get; set; } = 1;
    public string? Sort { get; set; }
    public bool ExportAll { get; set; }
}
public sealed record HayaReportFilterOption(int Id, string Label);
public sealed record HayaReportUserFilterOption(string Id, string Label);
public sealed class HayaReportModel
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public List<HayaReportKpi> Kpis { get; } = [];
    public List<HayaReportTable> Tables { get; } = [];
    public List<HayaReportChart> Charts { get; } = [];
    public HayaReportFilters Filters { get; set; } = new();
    public List<HayaReportFilterOption> Departments { get; } = [];
    public List<HayaReportFilterOption> Suppliers { get; } = [];
    public List<HayaReportFilterOption> Tenders { get; } = [];
    public List<HayaReportUserFilterOption> ReportCreators { get; } = [];
}

// Reports & Analytics (spec section 7): every figure is computed from the live SQL Server records; nothing is sample data.
// The same model feeds the pages and the Excel / PDF exports, so what a person sees is exactly what they download.
public sealed class HayaReportingService(ApplicationDbContext db)
{
    public static readonly (string Key, string Title, string Description)[] Catalog =
    [
        ("analytics-dashboard", "Analytics Dashboard", "Live system KPIs and trends across requests, tenders, suppliers, bids, evaluations and decisions."),
        ("purchase-requests", "Purchase Request Reports", "All requests, status and department breakdown, and average approval duration."),
        ("tenders", "Tender Reports", "Open, closed and awarded tenders, supplier participation, tenders without offers and evaluation delays."),
        ("suppliers", "Supplier Reports", "Registered, approved and suspended suppliers, legal verification, document expiry and performance."),
        ("evaluations", "Bids & Evaluation Reports", "Side-by-side offer comparison, compliance and excluded offers, evaluation scores and unified reports."),
        ("governance", "Approvals & Decision (Reports)", "Tracking committee recommendations, manager decisions, voting results, and the full audit trail for tenders."),
        ("management", "Top Committee Reports", "System-wide supplier participation, awards, outcomes and committee voting statistics."),
        ("reports-management", "Reports Management", "Search and filter every stored report version and its workflow status.")
    ];

    private static string N(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);
    private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "—";
    private static string Words(string s) => System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
    private static string Pct(int a, int b) => b == 0 ? "—" : (a * 100.0 / b).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    public Task<HayaReportModel> BuildAsync(string key, HayaReportFilters? filters = null)
    {
        filters ??= new HayaReportFilters();
        return key switch
        {
            "analytics-dashboard" => AnalyticsDashboard(filters),
            "purchase-requests" => PurchaseRequests(filters), "tenders" => Tenders(filters), "suppliers" => Suppliers(filters),
            "evaluations" => Evaluations(filters), "governance" => Governance(filters), "management" => Management(filters),
            "reports-management" => ReportsManagement(filters), _ => throw new KeyNotFoundException(key)
        };
    }

    private static HayaReportModel Start(string key) { var c = Catalog.Single(x => x.Key == key); return new HayaReportModel { Key = key, Title = c.Title, Description = c.Description }; }

    private async Task<HayaReportModel> AnalyticsDashboard(HayaReportFilters f)
    {
        var m = Start("analytics-dashboard"); m.Filters = f;
        var requests = await db.PurchaseRequests.AsNoTracking().GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var tenders = await db.Tenders.AsNoTracking().GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var suppliers = await db.Suppliers.AsNoTracking().GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var submittedBids = await db.Bids.AsNoTracking().CountAsync(x => x.Status == BidStatus.Submitted);
        var completedEvaluations = await db.Set<EvaluationSession>().AsNoTracking().CountAsync(x => x.State == SessionState.Finalized || x.State == SessionState.ManagerApproved || x.State == SessionState.Awarded);
        var pendingDecisions = await db.TenderReports.AsNoTracking().CountAsync(x => x.Status == ReportStatus.Submitted);
        int PR(params PurchaseRequestStatus[] s) => requests.Where(x => s.Contains(x.Status)).Sum(x => x.Count);
        int TN(params TenderStatus[] s) => tenders.Where(x => s.Contains(x.Status)).Sum(x => x.Count);
        int SP(SupplierStatus s) => suppliers.Where(x => x.Status == s).Sum(x => x.Count);
        var decidedRequests = requests.Where(x => x.Status != PurchaseRequestStatus.Draft).Sum(x => x.Count);
        var approvedRequests = PR(PurchaseRequestStatus.Accepted, PurchaseRequestStatus.Approved, PurchaseRequestStatus.ConvertedToTender);
        var approvalRate = decidedRequests == 0 ? "0%" : (approvedRequests * 100m / decidedRequests).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        m.Kpis.AddRange([new("Total Purchase Requests", requests.Sum(x => x.Count).ToString()), new("Pending Purchase Requests", PR(PurchaseRequestStatus.Submitted, PurchaseRequestStatus.UnderReview).ToString()), new("Approved Purchase Requests", approvedRequests.ToString()), new("Approval Rate", approvalRate), new("Total Tenders", tenders.Sum(x => x.Count).ToString()), new("Active Tender Rounds", (await db.TenderRounds.AsNoTracking().CountAsync(x => x.Status == TenderRoundStatus.Open)).ToString()), new("Awarded Tenders", TN(TenderStatus.Awarded).ToString()), new("Total Suppliers", suppliers.Sum(x => x.Count).ToString()), new("Approved Suppliers", SP(SupplierStatus.Approved).ToString()), new("Pending Suppliers", SP(SupplierStatus.Pending).ToString()), new("Total Submitted Bids", submittedBids.ToString()), new("Completed Evaluations", completedEvaluations.ToString()), new("Pending Decisions", pendingDecisions.ToString())]);
        m.Charts.Add(new("Purchase Requests by Status", requests.Select(x => new HayaReportChartPoint(Words(x.Status.ToString()), x.Count)).ToList()));
        m.Charts.Add(new("Tenders by Status", tenders.Select(x => new HayaReportChartPoint(Words(x.Status.ToString()), x.Count)).ToList()));
        m.Charts.Add(new("Supplier Registration Status", suppliers.Select(x => new HayaReportChartPoint(x.Status.ToString(), x.Count)).ToList()));
        var tenderTrend = await db.Tenders.AsNoTracking().GroupBy(x => new { x.CreatedAt.Year, x.CreatedAt.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        var supplierParticipation = await db.TenderInvitations.AsNoTracking().GroupBy(x => x.Supplier.CompanyName).Select(g => new { Label = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(12).ToListAsync();
        var bidTrend = await db.Bids.AsNoTracking().Where(x => x.SubmittedAt != null && x.Status == BidStatus.Submitted).GroupBy(x => new { x.SubmittedAt!.Value.Year, x.SubmittedAt.Value.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        var awardTrend = await db.Tenders.AsNoTracking().Where(x => x.AwardedAt != null).GroupBy(x => new { x.AwardedAt!.Value.Year, x.AwardedAt.Value.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        m.Charts.Add(new("Tender Activity Over Time", tenderTrend.Select(x => new HayaReportChartPoint($"{x.Year}-{x.Month:00}", x.Count)).ToList()));
        m.Charts.Add(new("Supplier Participation", supplierParticipation.Select(x => new HayaReportChartPoint(x.Label, x.Count)).ToList()));
        m.Charts.Add(new("Bid Submission Trends", bidTrend.Select(x => new HayaReportChartPoint($"{x.Year}-{x.Month:00}", x.Count)).ToList()));
        m.Charts.Add(new("Tender Award Statistics", awardTrend.Select(x => new HayaReportChartPoint($"{x.Year}-{x.Month:00}", x.Count)).ToList()));
        return m;
    }

    private async Task<HayaReportModel> PurchaseRequests(HayaReportFilters f)
    {
        var m = Start("purchase-requests"); m.Filters = f;
        var query = db.PurchaseRequests.AsNoTracking().Include(r => r.Department).AsQueryable();
        if (f.From != null) query = query.Where(r => r.CreatedAt >= f.From.Value);
        if (f.To != null) { var end = f.To.Value.Date.AddDays(1); query = query.Where(r => r.CreatedAt < end); }
        if (f.DepartmentId != null) query = query.Where(r => r.DepartmentId == f.DepartmentId);
        if (f.RequestStatus != null) query = query.Where(r => r.Status == f.RequestStatus);
        if (f.Priority != null) query = query.Where(r => r.Priority == f.Priority);
        if (!string.IsNullOrWhiteSpace(f.Search)) { var s = f.Search.Trim(); query = query.Where(r => r.Title.Contains(s) || ("PR-" + r.Id).Contains(s)); }
        var requests = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        var requestIds = requests.Select(r => r.Id).ToList();
        var reviews = await db.PurchaseRequestReviews.AsNoTracking().Where(r => requestIds.Contains(r.PurchaseRequestId) && r.ReviewedAt != null && r.Decision != ReviewDecision.Pending).Select(r => new { r.SubmittedAt, ReviewedAt = r.ReviewedAt!.Value }).ToListAsync();
        m.Departments.AddRange(await db.Departments.AsNoTracking().OrderBy(x => x.Name).Select(x => new HayaReportFilterOption(x.Id, x.Name)).ToListAsync());
        var avg = reviews.Count == 0 ? "—" : reviews.Average(r => (r.ReviewedAt - r.SubmittedAt).TotalHours).ToString("0.#", CultureInfo.InvariantCulture) + " h";
        int C(params PurchaseRequestStatus[] s) => requests.Count(r => s.Contains(r.Status));
        m.Kpis.AddRange([new("Total requests", requests.Count.ToString()), new("Submitted / under review", C(PurchaseRequestStatus.Submitted, PurchaseRequestStatus.UnderReview).ToString()),
            new("Accepted", C(PurchaseRequestStatus.Accepted, PurchaseRequestStatus.Approved, PurchaseRequestStatus.ConvertedToTender).ToString()), new("Returned / rejected", C(PurchaseRequestStatus.Returned, PurchaseRequestStatus.Rejected).ToString()),
            new("Average approval duration", avg)]);
        m.Tables.Add(new("Status breakdown", ["Status", "Requests", "Share"], requests.GroupBy(r => r.Status).OrderByDescending(g => g.Count()).Select(g => new[] { Words(g.Key.ToString()), g.Count().ToString(), Pct(g.Count(), requests.Count) }).ToList()));
        m.Tables.Add(new("By department", ["Department", "Requests", "Accepted", "Returned / rejected"], requests.GroupBy(r => r.Department.Name).OrderByDescending(g => g.Count())
            .Select(g => new[] { g.Key, g.Count().ToString(), g.Count(r => r.Status is PurchaseRequestStatus.Accepted or PurchaseRequestStatus.Approved or PurchaseRequestStatus.ConvertedToTender).ToString(), g.Count(r => r.Status is PurchaseRequestStatus.Returned or PurchaseRequestStatus.Rejected).ToString() }).ToList()));
        m.Tables.Add(new("Latest requests", ["Request", "Title", "Department", "Priority", "Status", "Created"], requests.Take(25).Select(r => new[] { $"PR-{r.Id:0000}", r.Title, r.Department.Name, r.Priority.ToString(), Words(r.Status.ToString()), D(r.CreatedAt) }).ToList()));
        m.Charts.Add(new("Purchase requests by status", requests.GroupBy(r => r.Status).Select(g => new HayaReportChartPoint(Words(g.Key.ToString()), g.Count())).ToList()));
        m.Charts.Add(new("Monthly request trend", requests.GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).Select(g => new HayaReportChartPoint($"{g.Key.Year}-{g.Key.Month:00}", g.Count())).ToList()));
        return m;
    }

    private async Task<HayaReportModel> Tenders(HayaReportFilters f)
    {
        var m = Start("tenders"); m.Filters = f;
        var tq = db.Tenders.AsNoTracking().AsQueryable();
        if (f.From != null) tq = tq.Where(t => t.CreatedAt >= f.From.Value);
        if (f.To != null) { var end = f.To.Value.Date.AddDays(1); tq = tq.Where(t => t.CreatedAt < end); }
        if (f.DepartmentId != null) tq = tq.Where(t => t.PurchaseRequest.DepartmentId == f.DepartmentId);
        if (f.TenderId != null) tq = tq.Where(t => t.Id == f.TenderId);
        if (f.TenderStatus != null) tq = tq.Where(t => t.Status == f.TenderStatus);
        if (!string.IsNullOrWhiteSpace(f.Search)) { var s = f.Search.Trim(); tq = tq.Where(t => t.Title.Contains(s) || ("T-" + t.Id).Contains(s)); }
        var tenders = await tq.OrderByDescending(t => t.CreatedAt).ToListAsync();
        var tenderIds = tenders.Select(t => t.Id).ToList();
        var rounds = await db.TenderRounds.AsNoTracking().Select(r => new { r.TenderId, r.RoundNumber, r.Status, r.ClosedAt, Invited = r.Invitations.Count, Responded = r.Invitations.Count(i => i.Bids.Any(b => b.Status == BidStatus.Submitted)),
            Offers = r.Invitations.SelectMany(i => i.Bids).Count(b => b.Status == BidStatus.Submitted) }).Where(r => tenderIds.Contains(r.TenderId)).ToListAsync();
        var invited = rounds.Sum(r => r.Invited); var responded = rounds.Sum(r => r.Responded);
        var zero = rounds.Where(r => r.Status is TenderRoundStatus.Closed or TenderRoundStatus.Failed or TenderRoundStatus.SentToEvaluation && r.Offers == 0).Select(r => r.TenderId).Distinct().Count();
        int C(params TenderStatus[] s) => tenders.Count(t => s.Contains(t.Status));
        m.Kpis.AddRange([new("Total tenders", tenders.Count.ToString()), new("Open tender rounds", rounds.Count(r => r.Status == TenderRoundStatus.Open).ToString()), new("Closed tender rounds", rounds.Count(r => r.Status is TenderRoundStatus.Closed or TenderRoundStatus.Failed or TenderRoundStatus.SentToEvaluation).ToString()), new("In evaluation / review", C(TenderStatus.Evaluation, TenderStatus.OfficerReview, TenderStatus.ManagerReview, TenderStatus.TopCommitteeReview).ToString()),
            new("Awarded", C(TenderStatus.Awarded).ToString()), new("Cancelled / rejected", C(TenderStatus.Cancelled, TenderStatus.Rejected).ToString()), new("Supplier participation rate", Pct(responded, invited)), new("Tenders with zero offers", zero.ToString())]);
        m.Tables.Add(new("Status breakdown", ["Status", "Tenders"], tenders.GroupBy(t => t.Status).Select(g => new[] { Words(g.Key.ToString()), g.Count().ToString() }).ToList()));
        m.Tables.Add(new("All tenders", ["Tender", "Title", "Status", "Estimated value", "Invited", "Offers", "Created", "Awarded"], tenders.Select(t =>
        {
            var rs = rounds.Where(r => r.TenderId == t.Id).ToList();
            return new[] { $"T-{t.Id:0000}", t.Title, Words(t.Status.ToString()), N(t.EstimatedValue), rs.Sum(r => r.Invited).ToString(), rs.Sum(r => r.Offers).ToString(), D(t.CreatedAt), D(t.AwardedAt) };
        }).ToList()));
        var now = DateTime.UtcNow;
        m.Tables.Add(new("Evaluation delay (tenders still in evaluation or review)", ["Tender", "Title", "Status", "Bidding closed", "Days waiting"],
            tenders.Where(t => t.Status is TenderStatus.Evaluation or TenderStatus.OfficerReview or TenderStatus.ManagerReview or TenderStatus.TopCommitteeReview).Select(t =>
            { var closed = rounds.Where(r => r.TenderId == t.Id).OrderByDescending(r => r.RoundNumber).Select(r => r.ClosedAt).FirstOrDefault(); return new[] { $"T-{t.Id:0000}", t.Title, Words(t.Status.ToString()), D(closed), closed == null ? "—" : ((int)(now - closed.Value).TotalDays).ToString() }; }).ToList()));
        m.Charts.Add(new("Tenders by status", tenders.GroupBy(t => t.Status).Select(g => new HayaReportChartPoint(Words(g.Key.ToString()), g.Count())).ToList()));
        m.Charts.Add(new("Tender activity over time", tenders.GroupBy(t => new { t.CreatedAt.Year, t.CreatedAt.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).Select(g => new HayaReportChartPoint($"{g.Key.Year}-{g.Key.Month:00}", g.Count())).ToList()));
        m.Tenders.AddRange(await db.Tenders.AsNoTracking().OrderBy(t => t.Title).Select(t => new HayaReportFilterOption(t.Id, t.Title)).ToListAsync());
        m.Departments.AddRange(await db.Departments.AsNoTracking().OrderBy(x => x.Name).Select(x => new HayaReportFilterOption(x.Id, x.Name)).ToListAsync());
        return m;
    }

    private async Task<HayaReportModel> Suppliers(HayaReportFilters f)
    {
        var m = Start("suppliers"); m.Filters = f;
        f.ExpiryDays = 30;
        f.ExpiryPage = Math.Max(1, f.ExpiryPage);
        f.PerformancePage = Math.Max(1, f.PerformancePage);
        var sq = db.Suppliers.AsNoTracking().AsQueryable();
        if (f.From != null) sq = sq.Where(x => x.CreatedAt >= f.From.Value.Date);
        if (f.To != null) { var end = f.To.Value.Date.AddDays(1); sq = sq.Where(x => x.CreatedAt < end); }
        if (f.SupplierId != null) sq = sq.Where(s => s.Id == f.SupplierId);
        if (f.VerificationStatus != null) sq = sq.Where(s => s.Documents.Any(d => d.VerificationStatus == f.VerificationStatus));
        if (!string.IsNullOrWhiteSpace(f.Search)) { var s = f.Search.Trim(); sq = sq.Where(x => x.CompanyName.Contains(s) || x.Email.Contains(s)); }
        var suppliers = await sq.OrderBy(s => s.CompanyName).ToListAsync();
        var supplierIds = suppliers.Select(s => s.Id).ToList();
        var docsQuery = db.Documents.AsNoTracking().Where(d => d.SupplierId != null && supplierIds.Contains(d.SupplierId.Value));
        if (f.VerificationStatus != null) docsQuery = docsQuery.Where(d => d.VerificationStatus == f.VerificationStatus);
        var docs = await docsQuery.Select(d => new { d.SupplierId, d.FileName, d.DocumentType, d.VerificationStatus, d.ExpiresAt }).ToListAsync();
        var invites = await db.TenderInvitations.AsNoTracking().Where(i => supplierIds.Contains(i.SupplierId)).Select(i => new { i.SupplierId, Offered = i.Bids.Any(b => b.Status == BidStatus.Submitted), Won = i.Bids.Any(b => b.Id == i.TenderRound.Tender.AwardedBidId) }).ToListAsync();
        const int threshold = 30;
        var today = DateTime.UtcNow.Date;
        var soon = today.AddDays(threshold);
        m.Kpis.AddRange([new("Registered suppliers", suppliers.Count.ToString()), new("Approved suppliers", suppliers.Count(s => s.Status == SupplierStatus.Approved).ToString()), new("Pending suppliers", suppliers.Count(s => s.Status == SupplierStatus.Pending).ToString()), new("Suspended suppliers", suppliers.Count(s => s.Status == SupplierStatus.Suspended).ToString()),
            new("Verified documents", docs.Count(d => d.VerificationStatus == DocumentVerificationStatus.Verified).ToString()), new("Documents expired or expiring soon", docs.Count(d => d.ExpiresAt != null && d.ExpiresAt.Value.Date <= soon).ToString())]);
        m.Tables.Add(new("Status breakdown", ["Status", "Suppliers"], suppliers.GroupBy(s => s.Status).Select(g => new[] { g.Key.ToString(), g.Count().ToString() }).ToList()));
        m.Tables.Add(new("By category", ["Category", "Suppliers"], suppliers.GroupBy(s => s.Category).OrderByDescending(g => g.Count()).Select(g => new[] { g.Key, g.Count().ToString() }).ToList()));
        var verification = suppliers.Select(s =>
        {
            var sd = docs.Where(d => d.SupplierId == s.Id).ToList();
            if (sd.Any(d => d.VerificationStatus == DocumentVerificationStatus.Rejected)) return "Rejected";
            if (sd.Count > 0 && sd.All(d => d.VerificationStatus == DocumentVerificationStatus.Verified)) return "Verified";
            return "Pending Verification";
        }).GroupBy(x => x).Select(g => new HayaReportChartPoint(g.Key, g.Count())).ToList();
        m.Tables.Add(new("Document verification status", ["Status", "Suppliers", "Rate"], verification.Select(x => new[] { x.Label, x.Value.ToString("0"), suppliers.Count == 0 ? "0%" : (x.Value * 100 / suppliers.Count).ToString("0.#", CultureInfo.InvariantCulture) + "%" }).ToList()));
        var pageSize = f.ExportAll ? int.MaxValue : 10;
        var performanceRows = suppliers.Select(s => { var i = invites.Where(x => x.SupplierId == s.Id).ToList(); return new[] { s.CompanyName, s.Status.ToString(), i.Count.ToString(), i.Count(x => x.Offered).ToString(), Pct(i.Count(x => x.Offered), i.Count), i.Count(x => x.Won).ToString() }; }).ToList();
        m.Tables.Add(new("Supplier performance", ["Supplier", "Status", "Invitations", "Offers submitted", "Participation", "Awards"], performanceRows.Skip((f.PerformancePage - 1) * pageSize).Take(pageSize).ToList(), performanceRows.Count, f.PerformancePage, pageSize));
        var expiryRows = docs.Where(d => d.ExpiresAt != null && d.ExpiresAt.Value.Date <= soon).OrderBy(d => d.ExpiresAt).Select(d => new[] { suppliers.FirstOrDefault(s => s.Id == d.SupplierId)?.CompanyName ?? "—", d.FileName, d.DocumentType ?? "—", D(d.ExpiresAt), d.ExpiresAt!.Value.Date < today ? "Expired" : "Expiring Soon" }).ToList();
        m.Tables.Add(new("Document expiry tracking", ["Supplier", "Document", "Type", "Expiry date", "Status"], expiryRows.Skip((f.ExpiryPage - 1) * pageSize).Take(pageSize).ToList(), expiryRows.Count, f.ExpiryPage, pageSize));
        m.Charts.Add(new("Document verification status", verification));
        m.Charts.Add(new("Supplier participation", suppliers.Select(s => new HayaReportChartPoint(s.CompanyName, invites.Count(i => i.SupplierId == s.Id && i.Offered))).OrderByDescending(x => x.Value).Take(12).ToList()));
        m.Suppliers.AddRange(await db.Suppliers.AsNoTracking().OrderBy(s => s.CompanyName).Select(s => new HayaReportFilterOption(s.Id, s.CompanyName)).ToListAsync());
        return m;
    }

    private async Task<HayaReportModel> Evaluations(HayaReportFilters f)
    {
        var m = Start("evaluations"); m.Filters = f;
        var allBids = db.Bids.AsNoTracking().AsQueryable();
        if (f.DepartmentId != null) allBids = allBids.Where(b => b.TenderInvitation.TenderRound.Tender.PurchaseRequest.DepartmentId == f.DepartmentId);
        if (f.TenderId != null) allBids = allBids.Where(b => b.TenderInvitation.TenderRound.TenderId == f.TenderId);
        if (f.SupplierId != null) allBids = allBids.Where(b => b.TenderInvitation.SupplierId == f.SupplierId);
        if (f.From != null) allBids = allBids.Where(b => b.CreatedAt >= f.From.Value);
        if (f.To != null) { var end = f.To.Value.Date.AddDays(1); allBids = allBids.Where(b => b.CreatedAt < end); }
        var bidStatuses = await allBids.GroupBy(b => b.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var bids = await allBids.Include(b => b.Items).Where(b => b.Status == BidStatus.Submitted)
            .Select(b => new { b.Id, b.ComplianceStatus, b.ComplianceNotes, b.DeliveryDays, b.ShippingCost, b.InstallationCost, b.MaintenanceCost, b.TaxAmount, b.OtherCost, Items = b.Items.Select(i => i.Quantity * i.UnitPrice).Sum(),
                Supplier = b.TenderInvitation.Supplier.CompanyName, Tender = b.TenderInvitation.TenderRound.Tender.Title, TenderId = b.TenderInvitation.TenderRound.TenderId, Awarded = b.TenderInvitation.TenderRound.Tender.AwardedBidId == b.Id,
                Score = db.EvaluationScores.Where(s => s.BidId == b.Id).Average(s => (decimal?)s.Score),
                TechnicalScore = db.EvaluationScores.Where(s => s.BidId == b.Id && s.EvaluationCriterion.Type == CriterionType.Technical).Average(s => (decimal?)s.Score),
                FinancialScore = db.EvaluationScores.Where(s => s.BidId == b.Id && s.EvaluationCriterion.Type == CriterionType.Financial).Average(s => (decimal?)s.Score) }).ToListAsync();
        decimal Total(dynamic b) => (decimal)b.Items + (decimal)b.ShippingCost + (decimal)b.InstallationCost + (decimal)b.MaintenanceCost + (decimal)b.TaxAmount + (decimal)b.OtherCost;
        var checkedOffers = bids.Count(b => b.ComplianceStatus != ComplianceStatus.NotChecked);
        var nonCompliant = bids.Count(b => b.ComplianceStatus == ComplianceStatus.NonCompliant);
        m.Kpis.AddRange([new("Total evaluations", bids.Count.ToString()), new("Compliant offers", bids.Count(b => b.ComplianceStatus == ComplianceStatus.Compliant).ToString()), new("Non-compliant offers", nonCompliant.ToString()), new("Excluded offers", nonCompliant.ToString()),
            new("Pending clarification", bids.Count(b => b.ComplianceStatus == ComplianceStatus.PendingClarification).ToString()), new("Evaluation completion rate", Pct(checkedOffers, bids.Count))]);
        m.Tables.Add(new("Compliance summary", ["Compliance", "Offers"], bids.GroupBy(b => b.ComplianceStatus).Select(g => new[] { Words(g.Key.ToString()), g.Count().ToString() }).ToList()));
        m.Tables.Add(new("Offer comparison by tender", ["Tender", "Supplier", "Total price", "Delivery days", "Compliance", "Technical score", "Financial score", "Final evaluation score", "Result"], bids.OrderBy(b => b.TenderId).ThenBy(b => (decimal)Total(b))
            .Select(b => new[] { b.Tender, b.Supplier, N(Total(b)), b.DeliveryDays?.ToString() ?? "—", Words(b.ComplianceStatus.ToString()), b.TechnicalScore is { } ts ? ts.ToString("0.##", CultureInfo.InvariantCulture) : "—", b.FinancialScore is { } fs ? fs.ToString("0.##", CultureInfo.InvariantCulture) : "—", b.Score is { } sc ? sc.ToString("0.##", CultureInfo.InvariantCulture) : "—", b.Awarded ? "Awarded" : "" }).ToList()));
        m.Tables.Add(new("Excluded offers", ["Tender", "Supplier", "Reason / notes"], bids.Where(b => b.ComplianceStatus == ComplianceStatus.NonCompliant).Select(b => new[] { b.Tender, b.Supplier, b.ComplianceNotes ?? "—" }).ToList()));
        var reports = await db.TenderReports.AsNoTracking().Where(r => r.ReportType == ReportType.FullEvaluation).Select(r => new { r.TenderRound.Tender.Title, r.VersionNumber, r.Status, r.SubmittedAt }).ToListAsync();
        m.Tables.Add(new("Unified evaluation reports", ["Tender", "Version", "Status", "Submitted"], reports.Select(r => new[] { r.Title, "v" + r.VersionNumber, r.Status.ToString(), D(r.SubmittedAt) }).ToList()));
        var scoreTypes = await db.EvaluationScores.AsNoTracking().Where(s => f.TenderId == null || s.Bid.TenderInvitation.TenderRound.TenderId == f.TenderId).GroupBy(s => s.EvaluationCriterion.Type).Select(g => new { Type = g.Key, Average = g.Average(x => x.Score), Count = g.Count() }).ToListAsync();
        m.Tables.Add(new("Technical and financial evaluation summary", ["Criterion type", "Recorded scores", "Average stored score"], scoreTypes.Select(x => new[] { x.Type.ToString(), x.Count.ToString(), x.Average.ToString("0.##", CultureInfo.InvariantCulture) }).ToList()));
        m.Charts.Add(new("Bid submission status", bidStatuses.Select(x => new HayaReportChartPoint(Words(x.Status.ToString()), x.Count)).ToList()));
        var evaluationTrend = await allBids.Where(b => b.Status == BidStatus.Submitted).GroupBy(b => new { b.CreatedAt.Year, b.CreatedAt.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        m.Charts.Add(new("Evaluation activity over time", evaluationTrend.Select(x => new HayaReportChartPoint($"{x.Year}-{x.Month:00}", x.Count)).ToList()));
        m.Tenders.AddRange(await db.Tenders.AsNoTracking().OrderBy(t => t.Title).Select(t => new HayaReportFilterOption(t.Id, t.Title)).ToListAsync());
        m.Suppliers.AddRange(await db.Suppliers.AsNoTracking().OrderBy(s => s.CompanyName).Select(s => new HayaReportFilterOption(s.Id, s.CompanyName)).ToListAsync());
        m.Departments.AddRange(await db.Departments.AsNoTracking().OrderBy(x => x.Name).Select(x => new HayaReportFilterOption(x.Id, x.Name)).ToListAsync());
        return m;
    }

    private async Task<HayaReportModel> Governance(HayaReportFilters f)
    {
        var m = Start("governance"); m.Filters = f;
        var reportsQuery = db.TenderReports.AsNoTracking().Where(r => r.ReportType == ReportType.FullEvaluation);
        if (f.From != null) reportsQuery = reportsQuery.Where(r => r.CreatedAt >= f.From.Value);
        if (f.To != null) { var end = f.To.Value.Date.AddDays(1); reportsQuery = reportsQuery.Where(r => r.CreatedAt < end); }
        if (f.TenderId != null) reportsQuery = reportsQuery.Where(r => r.TenderRound.TenderId == f.TenderId);
        var reports = await reportsQuery.Select(r => new { r.TenderRound.Tender.Title, r.VersionNumber, r.Status, r.ReviewedAt, Reviewer = r.ReviewedByUser == null ? null : r.ReviewedByUser.FullName, r.ReviewComment, RecommendedSupplier = r.RecommendedBid == null ? null : r.RecommendedBid.TenderInvitation.Supplier.CompanyName }).ToListAsync();
        var votesQuery = db.CommitteeVotes.AsNoTracking().Where(v => v.CommitteeRound.CommitteeType == CommitteeType.Top);
        if (f.TenderId != null) votesQuery = votesQuery.Where(v => v.CommitteeRound.TenderRound.TenderId == f.TenderId);
        var votes = await votesQuery.Select(v => new { Tender = v.CommitteeRound.TenderRound.Tender.Title, v.Decision, v.VotedAt }).ToListAsync();
        var awarded = await db.Tenders.AsNoTracking().Where(t => t.AwardedBidId != null).Select(t => new { t.Title, t.AwardedAt, t.EstimatedValue, Supplier = t.AwardedBid!.TenderInvitation.Supplier.CompanyName }).ToListAsync();
        var audit = await db.AuditLogs.AsNoTracking().Include(a => a.User).OrderByDescending(a => a.CreatedAt).Take(25).ToListAsync();
        var byAction = await db.AuditLogs.AsNoTracking().GroupBy(a => a.Action).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(15).ToListAsync();
        m.Kpis.AddRange([new("Reports accepted by Manager", reports.Count(r => r.Status == ReportStatus.Accepted).ToString()), new("Returned / rejected", reports.Count(r => r.Status is ReportStatus.Returned or ReportStatus.Rejected).ToString()),
            new("Top Committee approvals", votes.Count(v => v.Decision == VoteDecision.Approve).ToString()), new("Top Committee rejections", votes.Count(v => v.Decision == VoteDecision.Reject).ToString()),
            new("Awarded contracts", awarded.Count.ToString()), new("Audit entries", (await db.AuditLogs.CountAsync()).ToString())]);
        m.Tables.Add(new("Top Committee Recommendations", ["Tender", "Recommendation", "Votes", "Comment"], reports.Where(r => r.RecommendedSupplier != null).Select(r => new[] { r.Title, r.RecommendedSupplier!, votes.Count(v => v.Tender == r.Title).ToString(), r.ReviewComment ?? "—" }).ToList()));
        m.Tables.Add(new("Sourcing Manager Decisions", ["Tender", "Decision", "Decision date", "Comment"], reports.Where(r => r.ReviewedAt != null).Select(r => new[] { r.Title, r.Status.ToString(), D(r.ReviewedAt), r.ReviewComment ?? "—" }).ToList()));
        m.Tables.Add(new("Top Committee Voting Results", ["Tender", "Approve", "Reject", "Retender", "Votes recorded", "Result"], votes.GroupBy(v => v.Tender).Select(g => new[] { g.Key, g.Count(v => v.Decision == VoteDecision.Approve).ToString(), g.Count(v => v.Decision == VoteDecision.Reject).ToString(), g.Count(v => v.Decision == VoteDecision.Retender).ToString(), $"{g.Count()}/4", g.Count() < 4 ? $"Pending ({g.Count()}/4 votes)" : g.Any(v => v.Decision == VoteDecision.Retender) ? "Retender" : g.Count(v => v.Decision == VoteDecision.Approve) >= g.Count(v => v.Decision == VoteDecision.Reject) ? "Approved" : "Rejected" }).ToList()));
        m.Tables.Add(new("Awarded contracts", ["Tender", "Supplier", "Estimated value", "Awarded"], awarded.Select(a => new[] { a.Title, a.Supplier, N(a.EstimatedValue), D(a.AwardedAt) }).ToList()));
        m.Tables.Add(new("Audit trail by action", ["Action", "Entries"], byAction.Select(a => new[] { Words(a.Key), a.Count.ToString() }).ToList()));
        m.Tables.Add(new("Audit Trail", ["Action", "User", "Record", "Date & Time"], audit.Select(a => new[] { Words(a.Action), a.User?.FullName ?? "system", $"{a.EntityName} {a.EntityId}", a.CreatedAt.ToJordan("yyyy-MM-dd HH:mm") }).ToList()));
        m.Charts.Add(new("Top Committee voting summary", votes.GroupBy(v => v.Decision).Select(g => new HayaReportChartPoint(g.Key.ToString(), g.Count())).ToList()));
        m.Tenders.AddRange(await db.Tenders.AsNoTracking().OrderBy(x => x.Title).Select(x => new HayaReportFilterOption(x.Id, x.Title)).ToListAsync());
        return m;
    }

    private async Task<HayaReportModel> Management(HayaReportFilters f)
    {
        var m = Start("management"); m.Filters = f;
        var supplierQuery = db.Suppliers.AsNoTracking().AsQueryable();
        if (f.SupplierId != null) supplierQuery = supplierQuery.Where(x => x.Id == f.SupplierId);
        var suppliers = await supplierQuery.ToListAsync();
        var selectedSupplierIds = suppliers.Select(x => x.Id).ToList();
        var tenderQuery = db.Tenders.AsNoTracking().AsQueryable();
        if (f.From != null) tenderQuery = tenderQuery.Where(t => t.CreatedAt >= f.From.Value);
        if (f.To != null) { var end = f.To.Value.Date.AddDays(1); tenderQuery = tenderQuery.Where(t => t.CreatedAt < end); }
        if (f.TenderId != null) tenderQuery = tenderQuery.Where(t => t.Id == f.TenderId);
        var tenders = await tenderQuery.Select(t => new { t.Id, t.Status, t.EstimatedValue, t.AwardedBidId }).ToListAsync();
        var selectedTenderIds = tenders.Select(x => x.Id).ToList();
        var perSupplier = await db.TenderInvitations.AsNoTracking().Where(i => selectedSupplierIds.Contains(i.SupplierId) && selectedTenderIds.Contains(i.TenderRound.TenderId)).GroupBy(i => i.SupplierId).Select(g => new { SupplierId = g.Key, Invitations = g.Count(), Offers = g.Count(i => i.Bids.Any(b => b.Status == BidStatus.Submitted)),
            Awards = g.Count(i => i.Bids.Any(b => b.Id == i.TenderRound.Tender.AwardedBidId)), Value = g.Where(i => i.Bids.Any(b => b.Id == i.TenderRound.Tender.AwardedBidId)).Sum(i => (decimal?)i.TenderRound.Tender.EstimatedValue) ?? 0 }).ToListAsync();
        var zeroOfferTenders = await db.TenderRounds.AsNoTracking().Where(r => selectedTenderIds.Contains(r.TenderId) && (r.Status == TenderRoundStatus.Closed || r.Status == TenderRoundStatus.Failed || r.Status == TenderRoundStatus.SentToEvaluation))
            .Where(r => !r.Invitations.SelectMany(i => i.Bids).Any(b => b.Status == BidStatus.Submitted)).Select(r => r.TenderId).Distinct().CountAsync();
        var suppliersWithAwards = perSupplier.Count(x => x.Awards > 0);
        var totalOffers = perSupplier.Sum(x => x.Offers);
        var totalAwards = perSupplier.Sum(x => x.Awards);
        m.Kpis.AddRange([new("Registered suppliers", suppliers.Count.ToString()), new("Approved suppliers", suppliers.Count(s => s.Status == SupplierStatus.Approved).ToString()), new("Total tenders", tenders.Count.ToString()),
            new("Awarded tenders", tenders.Count(t => t.Status == TenderStatus.Awarded).ToString()), new("Cancelled / rejected", tenders.Count(t => t.Status is TenderStatus.Cancelled or TenderStatus.Rejected).ToString()),
            new("Zero offers", zeroOfferTenders.ToString()), new("Supplier award rate", Pct(totalAwards, totalOffers)), new("Repeat transaction rate", Pct(perSupplier.Count(x => x.Awards > 1), suppliersWithAwards))]);
        m.Tables.Add(new("Transactions with each supplier", ["Supplier", "Status", "Invitations", "Offers submitted", "Completed transactions", "Repeat transaction rate", "Awarded value"], suppliers.OrderBy(s => s.CompanyName).Select(s =>
        { var x = perSupplier.FirstOrDefault(p => p.SupplierId == s.Id); var awards = x?.Awards ?? 0; return new[] { s.CompanyName, s.Status.ToString(), (x?.Invitations ?? 0).ToString(), (x?.Offers ?? 0).ToString(), awards.ToString(), awards > 1 ? "100%" : "0%", N(x?.Value ?? 0) }; }).ToList()));
        m.Tables.Add(new("Tenders by status", ["Status", "Tenders"], tenders.GroupBy(t => t.Status).Select(g => new[] { Words(g.Key.ToString()), g.Count().ToString() }).ToList()));
        m.Tables.Add(new("Suppliers by status", ["Status", "Suppliers"], suppliers.GroupBy(s => s.Status).Select(g => new[] { g.Key.ToString(), g.Count().ToString() }).ToList()));
        var topVotes = await db.CommitteeVotes.AsNoTracking().Where(v => v.CommitteeRound.CommitteeType == CommitteeType.Top).GroupBy(v => v.Decision).Select(g => new { Decision = g.Key, Count = g.Count() }).ToListAsync();
        m.Kpis.Add(new("Retender votes", topVotes.Where(x => x.Decision == VoteDecision.Retender).Sum(x => x.Count).ToString()));
        m.Tables.Add(new("Top Committee voting summary", ["Decision", "Votes"], topVotes.Select(x => new[] { x.Decision.ToString(), x.Count.ToString() }).ToList()));
        var scoreSummary = await db.EvaluationScores.AsNoTracking().Where(s => selectedTenderIds.Contains(s.Bid.TenderInvitation.TenderRound.TenderId)).GroupBy(s => s.EvaluationCriterion.Type).Select(g => new { Type = g.Key, Count = g.Count(), Average = g.Average(x => x.Score) }).ToListAsync();
        m.Tables.Add(new("Technical & financial evaluation summary", ["Criterion type", "Recorded scores", "Average stored score"], scoreSummary.Select(x => new[] { Words(x.Type.ToString()), x.Count.ToString(), x.Average.ToString("0.##", CultureInfo.InvariantCulture) }).ToList()));
        var awardsByMonth = await db.Tenders.AsNoTracking().Where(t => t.AwardedAt != null).GroupBy(t => new { t.AwardedAt!.Value.Year, t.AwardedAt.Value.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        m.Charts.Add(new("Tender award trend", awardsByMonth.Select(x => new HayaReportChartPoint($"{x.Year}-{x.Month:00}", x.Count)).ToList()));
        m.Suppliers.AddRange(await db.Suppliers.AsNoTracking().OrderBy(x => x.CompanyName).Select(x => new HayaReportFilterOption(x.Id, x.CompanyName)).ToListAsync());
        m.Tenders.AddRange(await db.Tenders.AsNoTracking().OrderBy(x => x.Title).Select(x => new HayaReportFilterOption(x.Id, x.Title)).ToListAsync());
        return m;
    }

    private async Task<HayaReportModel> ReportsManagement(HayaReportFilters f)
    {
        var m = Start("reports-management"); m.Filters = f; f.ReportPage = Math.Max(1, f.ReportPage);
        var q = db.TenderReports.AsNoTracking().AsQueryable();
        if (f.From != null) q = q.Where(r => r.CreatedAt >= f.From.Value);
        if (f.To != null) { var end = f.To.Value.Date.AddDays(1); q = q.Where(r => r.CreatedAt < end); }
        if (f.DepartmentId != null) q = q.Where(r => r.TenderRound.Tender.PurchaseRequest.DepartmentId == f.DepartmentId);
        if (f.SupplierId != null) q = q.Where(r => r.RecommendedBid != null && r.RecommendedBid.TenderInvitation.SupplierId == f.SupplierId);
        if (f.TenderId != null) q = q.Where(r => r.TenderRound.TenderId == f.TenderId);
        if (f.RequestStatus != null) q = q.Where(r => r.TenderRound.Tender.PurchaseRequest.Status == f.RequestStatus);
        if (f.TenderStatus != null) q = q.Where(r => r.TenderRound.Tender.Status == f.TenderStatus);
        if (f.ReportType != null) q = q.Where(r => r.ReportType == f.ReportType);
        if (f.ReportStatus != null) q = q.Where(r => r.Status == f.ReportStatus);
        if (f.ReportVersion != null) q = q.Where(r => r.VersionNumber == f.ReportVersion);
        if (!string.IsNullOrWhiteSpace(f.CreatedByUserId)) q = q.Where(r => r.CreatedByUserId == f.CreatedByUserId);
        if (!string.IsNullOrWhiteSpace(f.Search)) { var s = f.Search.Trim(); q = q.Where(r => r.TenderRound.Tender.Title.Contains(s) || r.Content.Contains(s)); }
        q = f.Sort switch
        {
            "created_asc" => q.OrderBy(r => r.CreatedAt),
            "status" => q.OrderBy(r => r.Status).ThenByDescending(r => r.CreatedAt),
            "version" => q.OrderByDescending(r => r.VersionNumber).ThenByDescending(r => r.CreatedAt),
            _ => q.OrderByDescending(r => r.CreatedAt)
        };
        var allRows = await q.Select(r => new[] { r.Id.ToString(), r.TenderRound.Tender.Title, r.TenderRound.Tender.PurchaseRequest.Department.Name, r.ReportType.ToString(), "v" + r.VersionNumber, r.Status.ToString(), r.CreatedByUser.FullName, r.CreatedAt.ToString("yyyy-MM-dd") }).ToListAsync();
        const int pageSize = 10;
        var rows = f.ExportAll ? allRows : allRows.Skip((f.ReportPage - 1) * pageSize).Take(pageSize).ToList();
        m.Kpis.AddRange([new("Total reports", allRows.Count.ToString()), new("Submitted", allRows.Count(r => r[5] == ReportStatus.Submitted.ToString()).ToString()), new("Accepted", allRows.Count(r => r[5] == ReportStatus.Accepted.ToString()).ToString()), new("Returned", allRows.Count(r => r[5] == ReportStatus.Returned.ToString()).ToString()), new("Rejected", allRows.Count(r => r[5] == ReportStatus.Rejected.ToString()).ToString())]);
        m.Tables.Add(new("Stored report versions", ["ID", "Tender", "Department", "Type", "Version", "Status", "Created by", "Created"], rows, allRows.Count, f.ReportPage, pageSize));
        m.Charts.Add(new("Reports by status", allRows.GroupBy(r => r[5]).Select(g => new HayaReportChartPoint(g.Key, g.Count())).ToList()));
        m.Charts.Add(new("Report types distribution", allRows.GroupBy(r => r[3]).Select(g => new HayaReportChartPoint(g.Key, g.Count())).ToList()));
        m.Departments.AddRange(await db.Departments.AsNoTracking().OrderBy(x => x.Name).Select(x => new HayaReportFilterOption(x.Id, x.Name)).ToListAsync());
        m.Suppliers.AddRange(await db.Suppliers.AsNoTracking().OrderBy(x => x.CompanyName).Select(x => new HayaReportFilterOption(x.Id, x.CompanyName)).ToListAsync());
        m.Tenders.AddRange(await db.Tenders.AsNoTracking().OrderBy(x => x.Title).Select(x => new HayaReportFilterOption(x.Id, x.Title)).ToListAsync());
        // SQL Server/EF Core cannot translate the record Distinct + ordering projection here.
        // Read the two scalar columns first, then make the dropdown list in memory.
        var creatorRows = await db.TenderReports.AsNoTracking()
            .Where(r => r.CreatedByUser != null)
            .Select(r => new { r.CreatedByUserId, Name = r.CreatedByUser.FullName })
            .ToListAsync();
        m.ReportCreators.AddRange(creatorRows
            .GroupBy(x => x.CreatedByUserId)
            .Select(g => new HayaReportUserFilterOption(g.Key, g.First().Name))
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase));
        return m;
    }

    public async Task<HayaReportModel?> ReportDetailsAsync(int id)
    {
        var r = await db.TenderReports.AsNoTracking().Where(x => x.Id == id).Select(x => new
        {
            x.Id, Tender = x.TenderRound.Tender.Title, Department = x.TenderRound.Tender.PurchaseRequest.Department.Name,
            x.ReportType, x.VersionNumber, x.Status, Creator = x.CreatedByUser.FullName, x.CreatedAt, x.SubmittedAt,
            Reviewer = x.ReviewedByUser == null ? null : x.ReviewedByUser.FullName, x.ReviewedAt, x.ReviewComment,
            RecommendedSupplier = x.RecommendedBid == null ? null : x.RecommendedBid.TenderInvitation.Supplier.CompanyName,
            x.Content
        }).SingleOrDefaultAsync();
        if (r == null) return null;
        var m = new HayaReportModel { Key = "reports-management", Title = $"Report #{r.Id}", Description = $"{r.ReportType} report for {r.Tender}" };
        m.Kpis.AddRange([new("Status", r.Status.ToString()), new("Version", "v" + r.VersionNumber), new("Created", r.CreatedAt.ToJordan("yyyy-MM-dd HH:mm")), new("Submitted", r.SubmittedAt?.ToJordan("yyyy-MM-dd HH:mm") ?? "—")]);
        m.Tables.Add(new("Report details", ["Tender", "Department", "Type", "Created by", "Recommended supplier", "Reviewer", "Reviewed", "Review comment"], [new[] { r.Tender, r.Department, r.ReportType.ToString(), r.Creator, r.RecommendedSupplier ?? "—", r.Reviewer ?? "—", r.ReviewedAt?.ToJordan("yyyy-MM-dd HH:mm") ?? "—", r.ReviewComment ?? "—" }]));
        m.Tables.Add(new("Report content", ["Content"], [new[] { r.Content }]));
        return m;
    }
}
