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
<<<<<<< HEAD
        var ids = await MyTenders(userId).ToListAsync();
        var result = new List<TopCase>();
        foreach (var id in ids) { var c = await Build(userId, id); if (c != null) result.Add(c); }
        return result.OrderByDescending(c => c.AwaitingMyVote).ThenByDescending(c => c.TenderId).ToList();
=======
        var round = await RoundList(userId)
            .Include(r => r.TenderReport).ThenInclude(r => r!.CreatedByUser)
            .Include(r => r.TenderRound).ThenInclude(r => r.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(p => p.Department)
            .Include(r => r.TenderRound).ThenInclude(r => r.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(p => p.Items)
            .AsSplitQuery().SingleOrDefaultAsync(r => r.Id == id);
        if (round is null) return null;
        var tenderId = round.TenderRound.TenderId;
        var members = await db.CommitteeMembers.Include(m => m.User).Where(m => m.TenderId == tenderId).ToListAsync();
        var bids = await db.Bids.Where(b => b.TenderInvitation.TenderRoundId == round.TenderRoundId &&
                b.Status == BidStatus.Submitted && b.SubmittedAt != null &&
                !db.Bids.Any(newer => newer.TenderInvitationId == b.TenderInvitationId &&
                    newer.RevisionNumber > b.RevisionNumber && newer.SubmittedAt != null))
            .Include(b => b.TenderInvitation).ThenInclude(i => i.Supplier)
            .Include(b => b.Items).ThenInclude(i => i.TenderItem).AsSplitQuery().ToListAsync();
        var reports = await db.TenderReports.Where(r => r.TenderRoundId == round.TenderRoundId && r.Status != ReportStatus.Draft)
            .Include(r => r.CreatedByUser).OrderByDescending(r => r.VersionNumber).ToListAsync();
        var evaluations = await db.CommitteeRounds.Where(r => r.TenderRoundId == round.TenderRoundId && r.CommitteeType == CommitteeType.Evaluation)
            .Include(r => r.Votes).Include(r => r.Criteria).ThenInclude(c => c.Scores)
                .ThenInclude(s => s.CommitteeMember).ThenInclude(m => m.User).AsSplitQuery().ToListAsync();
        var topUserIds = members.Where(m => m.CommitteeType == CommitteeType.Top).Select(m => m.UserId).ToArray();
        var managerMembers = members.Where(m => m.CommitteeType == CommitteeType.Evaluation &&
            m.Responsibility == CommitteeResponsibility.SourcingManager).ToList();
        var manager = managerMembers.Count == 1 ? managerMembers[0].User : null;
        if (manager is not null && (!manager.IsActive || !await users.IsInRoleAsync(manager, SystemRoles.SourcingManager))) manager = null;
        var managerId = manager?.Id;
        var threadManagerId = managerId ?? round.TenderReport?.CreatedByUserId;
        var threads = await db.Clarifications.Where(c => c.TenderId == tenderId &&
            ((c.FromUserId == userId && c.ToUserId == threadManagerId) ||
             (c.ToUserId == userId && c.FromUserId == threadManagerId)))
            .Include(c => c.Messages).ThenInclude(m => m.SenderUser)
            .Include(c => c.Messages).ThenInclude(m => m.Documents)
            .Include(c => c.FromUser).Include(c => c.ToUser).AsSplitQuery().OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToListAsync();
        var threadKeys=threads.Select(t=>t.Id.ToString()).ToArray();
        var contextRows=await db.AuditLogs.Where(a=>a.Action=="TopClarificationContext" && a.EntityName=="Clarification" && threadKeys.Contains(a.EntityId)).Select(a=>new {a.EntityId,a.NewValues}).ToListAsync();
        var otherRoundThreads=contextRows.Where(a=>a.NewValues is not null && JsonSerializer.Deserialize<ClarificationContext>(a.NewValues)!.RoundId!=round.Id).Select(a=>a.EntityId).ToHashSet();
        threads.RemoveAll(t=>otherRoundThreads.Contains(t.Id.ToString()));
        var invited=await db.TenderInvitations.Where(i=>i.TenderRoundId==round.TenderRoundId).Select(i=>i.SupplierId).Distinct().CountAsync();
        var received=await db.TenderInvitations.CountAsync(i=>i.TenderRoundId==round.TenderRoundId && i.Bids.Any(b=>b.SubmittedAt!=null));
        var supplierIds = bids.Select(b => b.TenderInvitation.SupplierId).Distinct().ToArray();
        var bidIds = bids.Select(b => b.Id).ToArray();
        var reportIds = reports.Select(r => r.Id).ToArray();
        var requestId = round.TenderRound.Tender.PurchaseRequestId;
        var documents = await db.Documents.Where(d => d.TenderId == tenderId || d.PurchaseRequestId == requestId ||
            (d.BidId.HasValue && bidIds.Contains(d.BidId.Value)) ||
            (d.SupplierId.HasValue && supplierIds.Contains(d.SupplierId.Value)) ||
            (d.TenderReportId.HasValue && reportIds.Contains(d.TenderReportId.Value)))
            .AsNoTracking().OrderByDescending(d => d.UploadedAt).ThenByDescending(d => d.Id).ToListAsync();
        var details = new RoundDetails
        {
            Round = round, Members = members, InvitedSuppliers = invited, ReceivedBids = received, Bids = bids, Reports = reports, Evaluations = evaluations,
            Threads = threads, Documents = documents, Manager = manager, UserId = userId,
            FullReport = reports.FirstOrDefault(r => r.ReportType == ReportType.FullEvaluation),
            FailedAttempts = await db.TenderRounds.CountAsync(r => r.TenderId == tenderId &&
                r.RoundNumber < round.TenderRound.RoundNumber && r.Status == TenderRoundStatus.Failed),
            IsLatest = !await db.TenderRounds.AnyAsync(r => r.TenderId == tenderId && r.RoundNumber > round.TenderRound.RoundNumber) &&
                !await db.CommitteeRounds.AnyAsync(r => r.TenderRoundId == round.TenderRoundId &&
                    (r.CommitteeType == CommitteeType.Top || r.CommitteeType == TopCommitteeCompatibility.LegacyTop) && r.RoundNumber > round.RoundNumber) &&
                !await db.TenderReports.AnyAsync(r => r.TenderRoundId == round.TenderRoundId && r.ReportType == ReportType.ExecutiveSummary &&
                    r.SubmittedAt != null && r.VersionNumber > (round.TenderReport == null ? 0 : round.TenderReport.VersionNumber)),
            HasOpenClarifications = await db.Clarifications.AnyAsync(c => c.TenderId == tenderId && c.Status != ClarificationStatus.Closed &&
                ((c.FromUserId == managerId && topUserIds.Contains(c.ToUserId)) || (c.ToUserId == managerId && topUserIds.Contains(c.FromUserId))))
        };
        foreach (var executive in details.TopMembers)
        {
            var title = TopExecutiveRoles.Title(await users.GetRolesAsync(executive.User));
            if (title is not null) details.ExecutiveTitles[executive.UserId] = title;
        }
        details.OriginalSummaryPdf = details.Round.TenderReport is null ? null : documents.SingleOrDefault(d =>
            d.TenderReportId == details.Round.TenderReport.Id && d.DocumentType == "TopCommitteeOriginalSummaryPdf");
        details.FinalSignedSummaryPdf = details.Round.TenderReport is null ? null : documents.SingleOrDefault(d =>
            d.TenderReportId == details.Round.TenderReport.Id && d.DocumentType == "TopCommitteeFinalSignedPdf");
        details.Blockers = TopCommitteePolicy.Blockers(details);
        return details;
>>>>>>> be32000 (temp)
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
