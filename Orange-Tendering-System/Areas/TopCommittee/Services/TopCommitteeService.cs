using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

public sealed record TopCase(int TenderId, string Title, string Department, string Status, bool AwaitingMyVote, string? MyVote, int ReportId, DateTime? ReportSubmittedAt, string? Winner, int Approvals, int Votes, string? Outcome, DateTime? AwardedAt);
public sealed record TopOffer(string Supplier, decimal Total, int? DeliveryDays, string Compliance, decimal? AverageScore, int Rank, bool Recommended, bool Awarded);
public sealed class TopCaseDetails
{
    public required TopCase Case { get; init; }
    public string RequestTitle { get; init; } = ""; public string Justification { get; init; } = "";
    public List<(string Name, decimal Quantity, string Unit, string? Specifications)> Items { get; init; } = [];
    public List<TopOffer> Offers { get; init; } = [];
    public int ReportVersion { get; init; } public string ReportStatus { get; init; } = "";
    public string? ManagerName { get; init; } public DateTime? ManagerReviewedAt { get; init; } public string? ManagerComment { get; init; }
    public string? AiStatus { get; init; } public string? AiFindings { get; init; }
    public bool CanSeeCounts { get; init; }
}

// Read-only view of what a Top Committee member needs for the final decision (spec 4.7): the request, submitted offers, the unified report,
// the Sourcing Manager recommendation, AI analysis and bid history. Access is limited to tenders the signed-in member is assigned to as a Top
// member. Voting itself is NOT done here: it stays on the signed report page (TenderWorkflowService.TopVote), so signatures, the 3-of-4 rule
// and the Manager -> Officer -> Supplier relay cannot be bypassed.
public sealed class TopCommitteeService(ApplicationDbContext db)
{
    private IQueryable<int> MyTenders(string userId) => db.CommitteeMembers.AsNoTracking().Where(m => m.UserId == userId && m.CommitteeType == CommitteeType.Top).Select(m => m.TenderId);

    public async Task<List<TopCase>> Cases(string userId)
    {
        var ids = await MyTenders(userId).ToListAsync();
        var result = new List<TopCase>();
        foreach (var id in ids) { var c = await Build(userId, id); if (c != null) result.Add(c); }
        return result.OrderByDescending(c => c.AwaitingMyVote).ThenByDescending(c => c.TenderId).ToList();
    }

    private async Task<TopCase?> Build(string userId, int tenderId)
    {
        var t = await db.Tenders.AsNoTracking().Include(x => x.PurchaseRequest).ThenInclude(p => p.Department).SingleOrDefaultAsync(x => x.Id == tenderId);
        if (t == null) return null;
        var report = await db.TenderReports.AsNoTracking().Where(r => r.TenderRound.TenderId == tenderId && r.ReportType == ReportType.FullEvaluation).OrderByDescending(r => r.VersionNumber).ThenByDescending(r => r.Id).FirstOrDefaultAsync();
        var votes = report == null ? [] : await db.CommitteeVotes.AsNoTracking().Where(v => v.CommitteeRound.CommitteeType == CommitteeType.Top && v.CommitteeRound.TenderReportId == report.Id)
            .Select(v => new { v.Decision, v.CommitteeMember.UserId }).ToListAsync();
        var mine = votes.FirstOrDefault(v => v.UserId == userId)?.Decision;
        var winner = t.AwardedBidId != null ? await db.Bids.AsNoTracking().Where(b => b.Id == t.AwardedBidId).Select(b => b.TenderInvitation.Supplier.CompanyName).SingleOrDefaultAsync()
            : report?.RecommendedBidId == null ? null : await db.Bids.AsNoTracking().Where(b => b.Id == report.RecommendedBidId).Select(b => b.TenderInvitation.Supplier.CompanyName).SingleOrDefaultAsync();
        var outcome = t.Status == TenderStatus.Awarded ? "Awarded" : t.Status == TenderStatus.Rejected ? "Rejected" : t.Status == TenderStatus.Cancelled ? "Cancelled" : null;
        return new TopCase(t.Id, t.Title, t.PurchaseRequest.Department.Name, System.Text.RegularExpressions.Regex.Replace(t.Status.ToString(), "(?<=[a-z])(?=[A-Z])", " "),
            t.Status == TenderStatus.TopCommitteeReview && mine == null && report != null, mine?.ToString(), report?.Id ?? 0, report?.SubmittedAt, winner,
            votes.Count(v => v.Decision == VoteDecision.Approve), votes.Count, outcome, t.AwardedAt);
    }

    public async Task<TopCaseDetails?> Details(string userId, int tenderId)
    {
        if (!await MyTenders(userId).AnyAsync(id => id == tenderId)) return null;   // not assigned: indistinguishable from "does not exist"
        var c = await Build(userId, tenderId); if (c == null) return null;
        var t = await db.Tenders.AsNoTracking().Include(x => x.PurchaseRequest).ThenInclude(p => p.Items).SingleAsync(x => x.Id == tenderId);
        var round = await db.TenderRounds.AsNoTracking().Where(r => r.TenderId == tenderId).OrderByDescending(r => r.RoundNumber).FirstAsync();
        var bids = await db.Bids.AsNoTracking().Where(b => b.TenderInvitation.TenderRoundId == round.Id && b.Status == BidStatus.Submitted)
            .Select(b => new { b.Id, b.DeliveryDays, b.ComplianceStatus, b.ShippingCost, b.InstallationCost, b.MaintenanceCost, b.TaxAmount, b.OtherCost, Items = b.Items.Sum(i => i.Quantity * i.UnitPrice),
                Supplier = b.TenderInvitation.Supplier.CompanyName, Score = db.EvaluationScores.Where(s => s.BidId == b.Id).Average(s => (decimal?)s.Score) }).ToListAsync();
        var report = c.ReportId == 0 ? null : await db.TenderReports.AsNoTracking().Include(r => r.ReviewedByUser).Include(r => r.RecommendedBid).SingleAsync(r => r.Id == c.ReportId);
        var ranked = bids.OrderByDescending(b => b.Score ?? -1).Select((b, i) => (b, Rank: i + 1)).ToList();
        var ai = await db.Set<AiAnalysis>().AsNoTracking().Where(a => a.TenderRoundId == round.Id).OrderByDescending(a => a.Id).FirstOrDefaultAsync();
        return new TopCaseDetails
        {
            Case = c, RequestTitle = t.PurchaseRequest.Title, Justification = t.PurchaseRequest.Justification,
            Items = t.PurchaseRequest.Items.Select(i => (i.Name, i.Quantity, i.Unit, i.Specifications)).ToList(),
            Offers = ranked.Select(x => new TopOffer(x.b.Supplier, x.b.Items + x.b.ShippingCost + x.b.InstallationCost + x.b.MaintenanceCost + x.b.TaxAmount + x.b.OtherCost, x.b.DeliveryDays, x.b.ComplianceStatus.ToString(), x.b.Score, x.Rank,
                report?.RecommendedBidId == x.b.Id, t.AwardedBidId == x.b.Id)).ToList(),
            ReportVersion = report?.VersionNumber ?? 0, ReportStatus = report?.Status.ToString() ?? "—", ManagerName = report?.ReviewedByUser?.FullName, ManagerReviewedAt = report?.ReviewedAt, ManagerComment = report?.ReviewComment,
            AiStatus = ai?.Status, AiFindings = ai?.Findings,
            CanSeeCounts = c.MyVote != null || c.Outcome != null   // counts are shown only after the member has voted (independent decisions)
        };
    }
}
