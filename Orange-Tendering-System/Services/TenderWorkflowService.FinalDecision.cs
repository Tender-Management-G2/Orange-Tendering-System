using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// Final decision relay. Order is enforced by data, not by UI: Top Committee approvals (policy, 3 of 4) create the notice for the
// assigned Sourcing Manager; the Manager acknowledges and forwards it to the single Officer; only then can the Officer award and
// press "Notify Suppliers". Nothing here ever tells a supplier anything on its own.
public partial class TenderWorkflowService
{
    public const int MaxEmailAttempts = 3;

    private async Task<string?> AssignedManagerId(int tenderId, EvaluationSession? s) =>
        (await db.CommitteeMembers.Where(x => x.TenderId == tenderId && x.CommitteeType == CommitteeType.Evaluation && x.Responsibility == CommitteeResponsibility.SourcingManager)
            .Select(x => x.UserId).FirstOrDefaultAsync()) ?? s?.Report?.ReviewedByUserId;

    // Called while a Top vote is being recorded (inside the tender's serialized transaction) and when a Manager approves a tender
    // that does not need Top review. Creates at most one notice per tender report.
    private async Task RecordFinalDecision(Tender t, EvaluationSession s, int approves, int votes, bool managerDecisionIsFinal)
    {
        if (await db.Set<FinalDecisionNotice>().AnyAsync(x => x.TenderId == t.Id && x.TenderReportId == s.ReportId)) return;
        var managerId = await AssignedManagerId(t.Id, s);
        var notice = new FinalDecisionNotice
        {
            TenderId = t.Id, TenderReportId = s.ReportId, TopApprovals = approves, TopVotes = votes, CreatedAt = Now, ManagerUserId = managerId,
            Status = managerDecisionIsFinal ? FinalDecisionStatus.ForwardedToOfficer : FinalDecisionStatus.AwaitingManager,
            ManagerAcknowledgedAt = managerDecisionIsFinal ? Now : null, ManagerComment = managerDecisionIsFinal ? "Manager approval is the final decision for this tender." : null
        };
        db.Add(notice);
        if (managerDecisionIsFinal) await NotifyOfficer(t.Id, "Final decision forwarded", "The Sourcing Manager approved the recommendation. Review it, award the tender and notify suppliers.");
        else if (managerId != null) Notify(managerId, t.Id, "Top Committee final decision recorded", $"{approves} of {votes} Top Committee members approved. Acknowledge and forward the decision to the Sourcing Officer.");
    }

    public Task ManagerAcknowledge(int tenderId, string? comment, ClaimsPrincipal principal) => Change(tenderId, principal, "FinalDecisionAcknowledgedByManager", async (t, a) =>
    {
        var notice = await db.Set<FinalDecisionNotice>().Where(x => x.TenderId == tenderId).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        Require(notice != null, "No final decision is waiting for acknowledgement.");
        Require(await Role(a, SystemRoles.SourcingManager) && notice!.ManagerUserId == a.Id, "Only the Sourcing Manager assigned to this tender may acknowledge the final decision.");
        Require(notice.Status == FinalDecisionStatus.AwaitingManager, "This final decision was already acknowledged and forwarded.");
        notice.Status = FinalDecisionStatus.ForwardedToOfficer; notice.ManagerAcknowledgedAt = Now; notice.ManagerComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()[..Math.Min(comment.Trim().Length, 1000)];
        await NotifyOfficer(tenderId, "Final decision forwarded by the Sourcing Manager", "Review the decision, award the tender if required and use Notify Suppliers to announce the results.");
    });

    // Only the Officer, only after the tender is awarded and the Manager forwarded the decision, and only once.
    public Task NotifySuppliers(int tenderId, ClaimsPrincipal principal) => Change(tenderId, principal, "SupplierResultsAnnounced", async (t, a) =>
    {
        await Officer(t, a);
        var notice = await db.Set<FinalDecisionNotice>().Include(x => x.Deliveries).Where(x => x.TenderId == tenderId).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        Require(notice != null, "There is no final decision to announce yet.");
        Require(notice!.Status != FinalDecisionStatus.SuppliersNotified, "Suppliers were already notified about this decision. Use delivery retry for failed e-mails.");
        Require(notice.Status == FinalDecisionStatus.ForwardedToOfficer, "The assigned Sourcing Manager must acknowledge and forward the final decision first.");
        Require(t.Status == TenderStatus.Awarded && t.AwardedBidId != null, "Award the tender before notifying suppliers.");
        var winnerSupplierId = await db.Bids.Where(b => b.Id == t.AwardedBidId).Select(b => b.TenderInvitation.SupplierId).SingleAsync();
        var offers = await db.Bids.Where(b => b.TenderInvitation.TenderRound.TenderId == tenderId && b.Status == BidStatus.Submitted)
            .Select(b => new { b.Id, b.TenderInvitation.SupplierId }).ToListAsync();
        foreach (var offer in offers.GroupBy(o => o.SupplierId).Select(g => g.OrderByDescending(x => x.Id).First()))
        {
            var won = offer.SupplierId == winnerSupplierId;
            foreach (var userId in await db.Users.Where(u => u.SupplierId == offer.SupplierId && u.IsActive).Select(u => u.Id).ToListAsync())
            {
                // Each message concerns only the recipient's own company; no other supplier or price is ever mentioned.
                var n = new Notification { UserId = userId, Title = "Tender result", Link = $"/Supplier/Offers/Details/{offer.Id}", CreatedAt = Now,
                    Message = won ? "Congratulations: your company has been selected for this tender after all mandatory approvals."
                                  : "The tender has been awarded. Your company was not selected. Thank you for participating." };
                db.Notifications.Add(n);
                db.Add(new SupplierResultDelivery { FinalDecisionNotice = notice, TenderId = tenderId, SupplierId = offer.SupplierId, BidId = offer.Id, RecipientUserId = userId,
                    Outcome = won ? "Won" : "Unsuccessful", CreatedAt = Now, InAppStatus = "Delivered", EmailStatus = "Pending" });
            }
        }
        notice.Status = FinalDecisionStatus.SuppliersNotified; notice.SuppliersNotifiedAt = Now; notice.OfficerUserId = a.Id;
    });

    public async Task<List<FinalDecisionNotice>> FinalDecisions(ClaimsPrincipal principal)
    {
        var a = await Actor(principal);
        var q = db.Set<FinalDecisionNotice>().AsNoTracking().Include(x => x.Tender).Include(x => x.Deliveries).AsQueryable();
        if (await Role(a, SystemRoles.SourcingManager)) q = q.Where(x => x.ManagerUserId == a.Id);
        else Require(await IsOfficer(a), "Only the assigned Sourcing Manager or the Sourcing Officer may view final decisions.");
        return await q.OrderByDescending(x => x.CreatedAt).ToListAsync();
    }

    // E-mail step (after the notifications are committed): every delivery attempt is recorded; retries are capped.
    public async Task<List<(SupplierResultDelivery Delivery, string Email)>> DeliveriesToEmail(int tenderId, ClaimsPrincipal principal, bool retryOnly)
    {
        var a = await Actor(principal); Require(await IsOfficer(a), "Only the Sourcing Officer may send supplier e-mails.");
        var rows = await db.Set<SupplierResultDelivery>().Where(x => x.TenderId == tenderId && (retryOnly ? x.EmailStatus == "Failed" && x.EmailAttempts < MaxEmailAttempts : x.EmailStatus == "Pending")).ToListAsync();
        var result = new List<(SupplierResultDelivery, string)>();
        foreach (var d in rows) { var email = await db.Users.Where(u => u.Id == d.RecipientUserId).Select(u => u.Email).SingleAsync(); if (!string.IsNullOrWhiteSpace(email)) result.Add((d, email!)); }
        return result;
    }

    public async Task RecordEmailResult(int deliveryId, string status, string? error, ClaimsPrincipal principal)
    {
        var a = await Actor(principal);
        var d = await db.Set<SupplierResultDelivery>().SingleAsync(x => x.Id == deliveryId);
        d.EmailStatus = status; d.EmailAttempts += status == "NotConfigured" ? 0 : 1; d.LastError = error == null ? null : error[..Math.Min(error.Length, 500)];
        if (status == "Sent") d.EmailSentAt = Now;
        db.AuditLogs.Add(new AuditLog { UserId = a.Id, EntityName = "Tender", EntityId = d.TenderId.ToString(), Action = "SupplierResultEmail" + status, CreatedAt = Now,
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { deliveryId, d.EmailAttempts }) });
        await db.SaveChangesAsync();
    }
}
