using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

public sealed record ReportKpi(string Label, string Value);
public sealed record ReportTable(string Title, string[] Columns, List<string[]> Rows);
public sealed class ReportModel
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public List<ReportKpi> Kpis { get; } = [];
    public List<ReportTable> Tables { get; } = [];
}

// Reports & Analytics (spec section 7): every figure is computed from the live SQL Server records; nothing is sample data.
// The same model feeds the pages and the Excel / PDF exports, so what a person sees is exactly what they download.
public sealed class ReportingService(ApplicationDbContext db)
{
    public static readonly (string Key, string Title, string Description)[] Catalog =
    [
        ("purchase-requests", "Purchase Request Reports", "All requests, status and department breakdown, and average approval duration."),
        ("tenders", "Tender Reports", "Open, closed and awarded tenders, supplier participation, tenders without offers and evaluation delays."),
        ("suppliers", "Supplier Reports", "Registered, approved and suspended suppliers, legal verification, document expiry and performance."),
        ("evaluations", "Bids & Evaluation Reports", "Side-by-side offer comparison, compliance and excluded offers, evaluation scores and unified reports."),
        ("governance", "Approvals & Governance Reports", "Manager decisions, Top Committee voting records, awarded contracts and the audit trail."),
        ("management", "Management Statistics", "System-wide statistics and the number of transactions with each supplier (Top Committee).")
    ];

    private static string N(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);
    private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "—";
    private static string Words(string s) => System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
    private static string Pct(int a, int b) => b == 0 ? "—" : (a * 100.0 / b).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    public Task<ReportModel> BuildAsync(string key) => key switch
    {
        "purchase-requests" => PurchaseRequests(), "tenders" => Tenders(), "suppliers" => Suppliers(),
        "evaluations" => Evaluations(), "governance" => Governance(), "management" => Management(),
        _ => throw new KeyNotFoundException(key)
    };

    private static ReportModel Start(string key) { var c = Catalog.Single(x => x.Key == key); return new ReportModel { Key = key, Title = c.Title, Description = c.Description }; }

    private async Task<ReportModel> PurchaseRequests()
    {
        var m = Start("purchase-requests");
        var requests = await db.PurchaseRequests.AsNoTracking().Include(r => r.Department).OrderByDescending(r => r.CreatedAt).ToListAsync();
        var reviews = await db.PurchaseRequestReviews.AsNoTracking().Where(r => r.ReviewedAt != null && r.Decision != ReviewDecision.Pending).Select(r => new { r.SubmittedAt, ReviewedAt = r.ReviewedAt!.Value }).ToListAsync();
        var avg = reviews.Count == 0 ? "—" : reviews.Average(r => (r.ReviewedAt - r.SubmittedAt).TotalHours).ToString("0.#", CultureInfo.InvariantCulture) + " h";
        int C(params PurchaseRequestStatus[] s) => requests.Count(r => s.Contains(r.Status));
        m.Kpis.AddRange([new("Total requests", requests.Count.ToString()), new("Submitted / under review", C(PurchaseRequestStatus.Submitted, PurchaseRequestStatus.UnderReview).ToString()),
            new("Accepted", C(PurchaseRequestStatus.Accepted, PurchaseRequestStatus.Approved, PurchaseRequestStatus.ConvertedToTender).ToString()), new("Returned / rejected", C(PurchaseRequestStatus.Returned, PurchaseRequestStatus.Rejected).ToString()),
            new("Average approval duration", avg)]);
        m.Tables.Add(new("Status breakdown", ["Status", "Requests", "Share"], requests.GroupBy(r => r.Status).OrderByDescending(g => g.Count()).Select(g => new[] { Words(g.Key.ToString()), g.Count().ToString(), Pct(g.Count(), requests.Count) }).ToList()));
        m.Tables.Add(new("By department", ["Department", "Requests", "Accepted", "Returned / rejected"], requests.GroupBy(r => r.Department.Name).OrderByDescending(g => g.Count())
            .Select(g => new[] { g.Key, g.Count().ToString(), g.Count(r => r.Status is PurchaseRequestStatus.Accepted or PurchaseRequestStatus.Approved or PurchaseRequestStatus.ConvertedToTender).ToString(), g.Count(r => r.Status is PurchaseRequestStatus.Returned or PurchaseRequestStatus.Rejected).ToString() }).ToList()));
        m.Tables.Add(new("Latest requests", ["Request", "Title", "Department", "Priority", "Status", "Created"], requests.Take(25).Select(r => new[] { $"PR-{r.Id:0000}", r.Title, r.Department.Name, r.Priority.ToString(), Words(r.Status.ToString()), D(r.CreatedAt) }).ToList()));
        return m;
    }

    private async Task<ReportModel> Tenders()
    {
        var m = Start("tenders");
        var tenders = await db.Tenders.AsNoTracking().OrderByDescending(t => t.CreatedAt).ToListAsync();
        var rounds = await db.TenderRounds.AsNoTracking().Select(r => new { r.TenderId, r.RoundNumber, r.Status, r.ClosedAt, Invited = r.Invitations.Count, Responded = r.Invitations.Count(i => i.Bids.Any(b => b.Status == BidStatus.Submitted)),
            Offers = r.Invitations.SelectMany(i => i.Bids).Count(b => b.Status == BidStatus.Submitted) }).ToListAsync();
        var invited = rounds.Sum(r => r.Invited); var responded = rounds.Sum(r => r.Responded);
        var zero = rounds.Where(r => r.Status is TenderRoundStatus.Closed or TenderRoundStatus.Failed or TenderRoundStatus.SentToEvaluation && r.Offers == 0).Select(r => r.TenderId).Distinct().Count();
        int C(params TenderStatus[] s) => tenders.Count(t => s.Contains(t.Status));
        m.Kpis.AddRange([new("Total tenders", tenders.Count.ToString()), new("Open for bidding", C(TenderStatus.Bidding).ToString()), new("In evaluation / review", C(TenderStatus.Evaluation, TenderStatus.OfficerReview, TenderStatus.ManagerReview, TenderStatus.TopCommitteeReview).ToString()),
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
        return m;
    }

    private async Task<ReportModel> Suppliers()
    {
        var m = Start("suppliers");
        var suppliers = await db.Suppliers.AsNoTracking().OrderBy(s => s.CompanyName).ToListAsync();
        var docs = await db.Documents.AsNoTracking().Where(d => d.SupplierId != null).Select(d => new { d.SupplierId, d.FileName, d.DocumentType, d.VerificationStatus, d.ExpiresAt }).ToListAsync();
        var invites = await db.TenderInvitations.AsNoTracking().Select(i => new { i.SupplierId, Offered = i.Bids.Any(b => b.Status == BidStatus.Submitted), Won = i.Bids.Any(b => b.Id == i.TenderRound.Tender.AwardedBidId) }).ToListAsync();
        var soon = DateTime.UtcNow.AddDays(60);
        m.Kpis.AddRange([new("Registered suppliers", suppliers.Count.ToString()), new("Approved", suppliers.Count(s => s.Status == SupplierStatus.Approved).ToString()), new("Pending approval", suppliers.Count(s => s.Status == SupplierStatus.Pending).ToString()),
            new("Suspended / rejected", suppliers.Count(s => s.Status is SupplierStatus.Suspended or SupplierStatus.Rejected).ToString()),
            new("Documents awaiting verification", docs.Count(d => d.VerificationStatus is null or DocumentVerificationStatus.Pending).ToString()), new("Documents expired or expiring in 60 days", docs.Count(d => d.ExpiresAt != null && d.ExpiresAt < soon).ToString())]);
        m.Tables.Add(new("Status breakdown", ["Status", "Suppliers"], suppliers.GroupBy(s => s.Status).Select(g => new[] { g.Key.ToString(), g.Count().ToString() }).ToList()));
        m.Tables.Add(new("By category", ["Category", "Suppliers"], suppliers.GroupBy(s => s.Category).OrderByDescending(g => g.Count()).Select(g => new[] { g.Key, g.Count().ToString() }).ToList()));
        m.Tables.Add(new("Legal verification", ["Verification status", "Documents"], docs.GroupBy(d => d.VerificationStatus?.ToString() ?? "Pending").Select(g => new[] { g.Key, g.Count().ToString() }).ToList()));
        m.Tables.Add(new("Supplier performance", ["Supplier", "Status", "Invitations", "Offers submitted", "Participation", "Awards"], suppliers.Select(s =>
        { var i = invites.Where(x => x.SupplierId == s.Id).ToList(); return new[] { s.CompanyName, s.Status.ToString(), i.Count.ToString(), i.Count(x => x.Offered).ToString(), Pct(i.Count(x => x.Offered), i.Count), i.Count(x => x.Won).ToString() }; }).ToList()));
        m.Tables.Add(new("Document expiry tracking", ["Supplier", "Document", "Type", "Expires"], docs.Where(d => d.ExpiresAt != null && d.ExpiresAt < soon).OrderBy(d => d.ExpiresAt)
            .Select(d => new[] { suppliers.FirstOrDefault(s => s.Id == d.SupplierId)?.CompanyName ?? "—", d.FileName, d.DocumentType ?? "—", D(d.ExpiresAt) }).ToList()));
        return m;
    }

    private async Task<ReportModel> Evaluations()
    {
        var m = Start("evaluations");
        var bids = await db.Bids.AsNoTracking().Include(b => b.Items).Where(b => b.Status == BidStatus.Submitted)
            .Select(b => new { b.Id, b.ComplianceStatus, b.ComplianceNotes, b.DeliveryDays, b.ShippingCost, b.InstallationCost, b.MaintenanceCost, b.TaxAmount, b.OtherCost, Items = b.Items.Select(i => i.Quantity * i.UnitPrice).Sum(),
                Supplier = b.TenderInvitation.Supplier.CompanyName, Tender = b.TenderInvitation.TenderRound.Tender.Title, TenderId = b.TenderInvitation.TenderRound.TenderId, Awarded = b.TenderInvitation.TenderRound.Tender.AwardedBidId == b.Id,
                Score = db.EvaluationScores.Where(s => s.BidId == b.Id).Average(s => (decimal?)s.Score) }).ToListAsync();
        decimal Total(dynamic b) => (decimal)b.Items + (decimal)b.ShippingCost + (decimal)b.InstallationCost + (decimal)b.MaintenanceCost + (decimal)b.TaxAmount + (decimal)b.OtherCost;
        m.Kpis.AddRange([new("Submitted offers", bids.Count.ToString()), new("Compliant", bids.Count(b => b.ComplianceStatus == ComplianceStatus.Compliant).ToString()), new("Non-compliant (excluded)", bids.Count(b => b.ComplianceStatus == ComplianceStatus.NonCompliant).ToString()),
            new("Pending clarification", bids.Count(b => b.ComplianceStatus == ComplianceStatus.PendingClarification).ToString()), new("Not checked yet", bids.Count(b => b.ComplianceStatus == ComplianceStatus.NotChecked).ToString())]);
        m.Tables.Add(new("Compliance summary", ["Compliance", "Offers"], bids.GroupBy(b => b.ComplianceStatus).Select(g => new[] { Words(g.Key.ToString()), g.Count().ToString() }).ToList()));
        m.Tables.Add(new("Offer comparison by tender", ["Tender", "Supplier", "Total price", "Delivery days", "Compliance", "Average score", "Result"], bids.OrderBy(b => b.TenderId).ThenBy(b => (decimal)Total(b))
            .Select(b => new[] { b.Tender, b.Supplier, N(Total(b)), b.DeliveryDays?.ToString() ?? "—", Words(b.ComplianceStatus.ToString()), b.Score is { } sc ? sc.ToString("0.##", CultureInfo.InvariantCulture) : "—", b.Awarded ? "Awarded" : "" }).ToList()));
        m.Tables.Add(new("Excluded offers", ["Tender", "Supplier", "Reason / notes"], bids.Where(b => b.ComplianceStatus == ComplianceStatus.NonCompliant).Select(b => new[] { b.Tender, b.Supplier, b.ComplianceNotes ?? "—" }).ToList()));
        var reports = await db.TenderReports.AsNoTracking().Where(r => r.ReportType == ReportType.FullEvaluation).Select(r => new { r.TenderRound.Tender.Title, r.VersionNumber, r.Status, r.SubmittedAt }).ToListAsync();
        m.Tables.Add(new("Unified evaluation reports", ["Tender", "Version", "Status", "Submitted"], reports.Select(r => new[] { r.Title, "v" + r.VersionNumber, r.Status.ToString(), D(r.SubmittedAt) }).ToList()));
        return m;
    }

    private async Task<ReportModel> Governance()
    {
        var m = Start("governance");
        var reports = await db.TenderReports.AsNoTracking().Where(r => r.ReportType == ReportType.FullEvaluation).Select(r => new { r.TenderRound.Tender.Title, r.VersionNumber, r.Status, r.ReviewedAt, Reviewer = r.ReviewedByUser == null ? null : r.ReviewedByUser.FullName, r.ReviewComment }).ToListAsync();
        var votes = await db.CommitteeVotes.AsNoTracking().Where(v => v.CommitteeRound.CommitteeType == CommitteeType.Top).Select(v => new { Tender = v.CommitteeRound.TenderRound.Tender.Title, v.Decision, v.VotedAt }).ToListAsync();
        var awarded = await db.Tenders.AsNoTracking().Where(t => t.AwardedBidId != null).Select(t => new { t.Title, t.AwardedAt, t.EstimatedValue, Supplier = t.AwardedBid!.TenderInvitation.Supplier.CompanyName }).ToListAsync();
        var audit = await db.AuditLogs.AsNoTracking().Include(a => a.User).OrderByDescending(a => a.CreatedAt).Take(25).ToListAsync();
        var byAction = await db.AuditLogs.AsNoTracking().GroupBy(a => a.Action).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(15).ToListAsync();
        m.Kpis.AddRange([new("Reports accepted by Manager", reports.Count(r => r.Status == ReportStatus.Accepted).ToString()), new("Returned / rejected", reports.Count(r => r.Status is ReportStatus.Returned or ReportStatus.Rejected).ToString()),
            new("Top Committee approvals", votes.Count(v => v.Decision == VoteDecision.Approve).ToString()), new("Top Committee rejections", votes.Count(v => v.Decision == VoteDecision.Reject).ToString()),
            new("Awarded contracts", awarded.Count.ToString()), new("Audit entries", (await db.AuditLogs.CountAsync()).ToString())]);
        m.Tables.Add(new("Sourcing Manager decisions", ["Tender", "Version", "Decision", "Reviewed", "Reviewer", "Comment"], reports.Where(r => r.ReviewedAt != null).Select(r => new[] { r.Title, "v" + r.VersionNumber, r.Status.ToString(), D(r.ReviewedAt), r.Reviewer ?? "—", r.ReviewComment ?? "—" }).ToList()));
        m.Tables.Add(new("Top Committee voting records", ["Tender", "Approve", "Reject", "Last vote"], votes.GroupBy(v => v.Tender).Select(g => new[] { g.Key, g.Count(v => v.Decision == VoteDecision.Approve).ToString(), g.Count(v => v.Decision == VoteDecision.Reject).ToString(), D(g.Max(v => v.VotedAt)) }).ToList()));
        m.Tables.Add(new("Awarded contracts", ["Tender", "Supplier", "Estimated value", "Awarded"], awarded.Select(a => new[] { a.Title, a.Supplier, N(a.EstimatedValue), D(a.AwardedAt) }).ToList()));
        m.Tables.Add(new("Audit trail by action", ["Action", "Entries"], byAction.Select(a => new[] { Words(a.Key), a.Count.ToString() }).ToList()));
        m.Tables.Add(new("Latest audit entries", ["Time (UTC)", "User", "Action", "Record"], audit.Select(a => new[] { a.CreatedAt.ToString("yyyy-MM-dd HH:mm"), a.User?.FullName ?? "system", Words(a.Action), $"{a.EntityName} {a.EntityId}" }).ToList()));
        return m;
    }

    private async Task<ReportModel> Management()
    {
        var m = Start("management");
        var suppliers = await db.Suppliers.AsNoTracking().ToListAsync();
        var tenders = await db.Tenders.AsNoTracking().Select(t => new { t.Status, t.EstimatedValue, t.AwardedBidId }).ToListAsync();
        var perSupplier = await db.TenderInvitations.AsNoTracking().GroupBy(i => i.SupplierId).Select(g => new { SupplierId = g.Key, Invitations = g.Count(), Offers = g.Count(i => i.Bids.Any(b => b.Status == BidStatus.Submitted)),
            Awards = g.Count(i => i.Bids.Any(b => b.Id == i.TenderRound.Tender.AwardedBidId)), Value = g.Where(i => i.Bids.Any(b => b.Id == i.TenderRound.Tender.AwardedBidId)).Sum(i => (decimal?)i.TenderRound.Tender.EstimatedValue) ?? 0 }).ToListAsync();
        m.Kpis.AddRange([new("Registered suppliers", suppliers.Count.ToString()), new("Approved suppliers", suppliers.Count(s => s.Status == SupplierStatus.Approved).ToString()), new("Total tenders", tenders.Count.ToString()),
            new("Awarded tenders", tenders.Count(t => t.Status == TenderStatus.Awarded).ToString()), new("Awarded value (estimated)", N(tenders.Where(t => t.Status == TenderStatus.Awarded).Sum(t => t.EstimatedValue))),
            new("Total supplier transactions (invitations)", perSupplier.Sum(x => x.Invitations).ToString())]);
        m.Tables.Add(new("Transactions with each supplier", ["Supplier", "Status", "Invitations", "Offers submitted", "Awards", "Awarded value"], suppliers.OrderBy(s => s.CompanyName).Select(s =>
        { var x = perSupplier.FirstOrDefault(p => p.SupplierId == s.Id); return new[] { s.CompanyName, s.Status.ToString(), (x?.Invitations ?? 0).ToString(), (x?.Offers ?? 0).ToString(), (x?.Awards ?? 0).ToString(), N(x?.Value ?? 0) }; }).ToList()));
        m.Tables.Add(new("Tenders by status", ["Status", "Tenders"], tenders.GroupBy(t => t.Status).Select(g => new[] { Words(g.Key.ToString()), g.Count().ToString() }).ToList()));
        m.Tables.Add(new("Suppliers by status", ["Status", "Suppliers"], suppliers.GroupBy(s => s.Status).Select(g => new[] { g.Key.ToString(), g.Count().ToString() }).ToList()));
        return m;
    }
}
