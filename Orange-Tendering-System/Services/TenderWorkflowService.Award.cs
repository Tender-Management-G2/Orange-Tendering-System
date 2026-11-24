using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// Final award. The Top Committee is the final authority: the fourth distinct approval of the same signed report version
// awards the recommended offer automatically, in the same serializable transaction (tender row lock) as that vote.
// There is no Sourcing Manager acknowledgement and no separate Officer "award" step afterwards.
public partial class TenderWorkflowService
{
    public const int UnanimousTopApprovals = 4;

    private async Task AwardOnUnanimousApproval(Tender t, EvaluationSession s, CommitteeRound cr, ApplicationUser actor)
    {
        // Idempotency: a retried or concurrent fourth vote finds the tender already awarded or the round already completed.
        Require(t.AwardedBidId == null && t.Status == TenderStatus.TopCommitteeReview && cr.Status == CommitteeRoundStatus.InProgress, "This tender was already awarded.");
        Require(!await db.Set<FinalDecisionNotice>().AnyAsync(x => x.TenderId == t.Id && x.TenderReportId == s.ReportId), "A final decision already exists for this report version.");

        var members = await Members(t.Id, CommitteeType.Top);
        var votes = await db.CommitteeVotes.Include(x => x.CommitteeMember).Where(x => x.CommitteeRoundId == cr.Id).ToListAsync();
        Require(members.Count == UnanimousTopApprovals && votes.Count == UnanimousTopApprovals && votes.All(v => v.Decision == VoteDecision.Approve)
            && votes.Select(v => v.CommitteeMemberId).Distinct().Count() == UnanimousTopApprovals && votes.All(v => members.Any(m => m.Id == v.CommitteeMemberId)),
            "Four distinct assigned Top Committee members must approve the same report version.");
        var topSignatures = await db.Set<ReportSignature>().Where(x => x.TenderReportId == s.ReportId && x.Stage == ReportSigningStage.Top && x.InvalidatedAt == null).ToListAsync();
        Require(votes.All(v => topSignatures.Any(x => x.UserId == v.CommitteeMember.UserId && x.Decision == nameof(VoteDecision.Approve))),
            "Every approval must carry its signature on the current report version.");
        await reportDocuments.RequireCommitteeSignatures(s.Report!, s);
        Require(await db.Set<ReportSignature>().AnyAsync(x => x.TenderReportId == s.ReportId && x.Stage == ReportSigningStage.Manager && x.Decision == "Approve" && x.InvalidatedAt == null),
            "The Sourcing Manager's signature on the current report version is required.");

        // The recommended offer must still be the unique calculated rank 1 and remain eligible.
        var ranking = await Ranking(s);
        Require(ranking.Count(x => x.Rank == 1) == 1 && s.Report!.RecommendedBidId == ranking.Single(x => x.Rank == 1).BidId,
            "Exact top-score ties or a changed ranking block the award; a documented resolution policy is required.");
        var bid = await db.Bids.Include(x => x.TenderInvitation).ThenInclude(x => x.Supplier).SingleAsync(x => x.Id == s.Report!.RecommendedBidId);
        Require(bid.Status == BidStatus.Submitted && bid.ComplianceStatus == ComplianceStatus.Compliant && s.EligibleBids.Any(x => x.BidId == bid.Id),
            "Only an eligible, compliant submitted offer can be awarded.");
        Require(bid.TenderInvitation.Supplier.Status == SupplierStatus.Approved, "The recommended supplier is no longer an approved supplier. The award is blocked.");

        cr.Status = CommitteeRoundStatus.Approved; cr.CompletedAt = Now;
        t.AwardedBidId = bid.Id; t.AwardedAt = Now; t.ClosedAt = Now; t.Status = TenderStatus.Awarded; t.AwardedByUserId = actor.Id;
        s.State = SessionState.Awarded;
        t.AwardApprovalEvidenceJson = JsonSerializer.Serialize(new { Mode = "AutomaticOnUnanimousTopApproval", EvaluationSessionId = s.Id, s.ReportId, s.Report!.VersionNumber,
            ReportHash = s.Report.PdfSha256, CommitteeRoundId = cr.Id, WeightApproval = s.WeightRoundId, Votes = votes.Select(x => new { x.CommitteeMemberId, x.Decision, x.VotedAt }) });

        var managerId = await AssignedManagerId(t.Id, s);
        var notice = new FinalDecisionNotice { TenderId = t.Id, TenderReportId = s.ReportId, Outcome = FinalDecisionOutcome.Award, TopApprovals = votes.Count, TopVotes = votes.Count,
            CreatedAt = Now, ManagerUserId = managerId, ManagerComment = "Not required: unanimous Top Committee approval is the final decision.",
            Status = FinalDecisionStatus.SuppliersNotified, SuppliersNotifiedAt = Now };
        db.Add(notice);

        // Suppliers: each message concerns only the recipient's own company (no competitor, price, score or vote).
        var winnerSupplierId = bid.TenderInvitation.SupplierId;
        var offers = await db.Bids.Where(b => b.TenderInvitation.TenderRoundId == s.TenderRoundId && b.Status == BidStatus.Submitted)
            .Select(b => new { b.Id, b.TenderInvitation.SupplierId }).ToListAsync();
        foreach (var offer in offers.GroupBy(o => o.SupplierId).Select(g => g.OrderByDescending(x => x.Id).First()))
        {
            var won = offer.SupplierId == winnerSupplierId;
            foreach (var userId in await db.Users.Where(u => u.SupplierId == offer.SupplierId && u.IsActive).Select(u => u.Id).ToListAsync())
            {
                db.Notifications.Add(new Notification { UserId = userId, Title = "Tender result", Link = $"/Supplier/Offers/Details/{offer.Id}", CreatedAt = Now,
                    Message = won ? "Congratulations: your company has been selected for this tender after all mandatory approvals."
                                  : "The tender has been awarded. Your company was not selected. Thank you for participating." });
                db.Add(new SupplierResultDelivery { FinalDecisionNotice = notice, TenderId = t.Id, SupplierId = offer.SupplierId, BidId = offer.Id, RecipientUserId = userId,
                    Outcome = won ? "Won" : "Unsuccessful", CreatedAt = Now, InAppStatus = "Delivered", EmailStatus = "Pending" });
            }
        }

        var winner = bid.TenderInvitation.Supplier.CompanyName;
        var officerId = (await GlobalOfficer()).Id;
        if (managerId != null)
        {
            Notification AwardNotification(string userId) => new()
            {
                UserId = userId, Title = "Tender award report", Link = $"/Workflow/Details/{t.Id}", CreatedAt = Now,
                Message = $"T-{t.Id:0000} · {t.Title}\nWinning supplier: {winner}\nThe Top Committee approved the award."
            };
            db.InternalAwardReports.Add(new InternalAwardReport
            {
                FinalDecisionNotice = notice, TenderId = t.Id, TenderTitle = t.Title, SupplierName = winner, AwardedAt = Now,
                OfficerNotification = AwardNotification(officerId), ManagerNotification = AwardNotification(managerId)
            });
        }
        // Award notifications: only the Sourcing Officer and the Sourcing Manager receive the internal award report (above), the winner gets the
        // congratulation and each losing supplier its own regret message (suppliers loop above). Committee members are not notified.
        // Fallback only when no Manager is assigned: the Officer still gets the award notice.
        if (managerId == null)
            Notify(officerId, t.Id, "Tender awarded", $"The Top Committee approved the recommendation unanimously (4 of 4). The tender is awarded to {winner} and closed.");
        var pr = await db.PurchaseRequests.AsNoTracking().SingleAsync(x => x.Id == t.PurchaseRequestId);
        var departmentUsers = (await users.GetUsersInRoleAsync(SystemRoles.RequestingDepartment)).Where(u => u.IsActive && u.DepartmentId == pr.DepartmentId)
            .Select(u => u.Id).Append(pr.CreatedByUserId).Distinct().ToList();
        foreach (var userId in departmentUsers)
            db.Notifications.Add(new Notification { UserId = userId, Title = "Purchase request awarded", CreatedAt = Now, Link = $"/Department/PurchaseRequests/Details/{pr.Id}",
                Message = $"PR-{pr.Id}: the tender \"{t.Title}\" was awarded to {winner} after unanimous Top Committee approval." });
        Audit(actor, t.Id, "TenderAwardedAutomatically", new { BidId = bid.Id, SupplierId = winnerSupplierId, s.ReportId, CommitteeRoundId = cr.Id, Approvals = votes.Count });
    }

    // Supplier e-mails for an award the system just committed. Not an authorization boundary: it only reads delivery rows
    // created inside the award transaction and records each attempt.
    public async Task<List<(SupplierResultDelivery Delivery, string Email)>> PendingAwardEmails(int tenderId)
    {
        var rows = await db.Set<SupplierResultDelivery>().Where(x => x.TenderId == tenderId && x.EmailStatus == "Pending" &&
            x.FinalDecisionNotice.Status == FinalDecisionStatus.SuppliersNotified && x.FinalDecisionNotice.Outcome == FinalDecisionOutcome.Award).ToListAsync();
        var result = new List<(SupplierResultDelivery, string)>();
        foreach (var d in rows) { var email = await db.Users.Where(u => u.Id == d.RecipientUserId).Select(u => u.Email).SingleAsync(); if (!string.IsNullOrWhiteSpace(email)) result.Add((d, email!)); }
        return result;
    }

    public async Task RecordSystemEmailResult(int deliveryId, string status, string? error)
    {
        var d = await db.Set<SupplierResultDelivery>().SingleAsync(x => x.Id == deliveryId);
        d.EmailStatus = status; d.EmailAttempts += status == "NotConfigured" ? 0 : 1; d.LastError = error == null ? null : error[..Math.Min(error.Length, 500)];
        if (status == "Sent") d.EmailSentAt = Now;
        db.AuditLogs.Add(new AuditLog { EntityName = "Tender", EntityId = d.TenderId.ToString(), Action = "SupplierResultEmail" + status, CreatedAt = Now,
            NewValues = JsonSerializer.Serialize(new { deliveryId, d.EmailAttempts }) });
        await db.SaveChangesAsync();
    }
}
