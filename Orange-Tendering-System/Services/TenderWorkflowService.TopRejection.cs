using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Top Committee final decision: exactly three actions. APPROVE keeps the existing relay (TopVote/RecordFinalDecision/Award).
// REJECT (the company no longer needs the tender) and RE-TENDER each stop the current Top approval at once, even after earlier
// approvals, and travel Top Committee -> assigned Sourcing Manager -> the single Sourcing Officer:
//   Reject    -> the Officer notifies participating suppliers and the requesting department; the tender is cancelled.
//   Re-Tender -> the Officer edits permitted details, the deadline and the invitation list (>= 3 suppliers) and publishes the next
//                round of the same tender (never beyond round MaxRounds); compliance, AI, evaluation and approvals restart.
// Votes, signatures, bids, evaluations and audit history are never removed. Every step runs in Change(): one SQL Server
// serializable transaction holding the tender row lock.
public partial class TenderWorkflowService
{
    public static string RejectionReasonText(TopRejectionReason? reason) => reason switch
    {
        TopRejectionReason.NotNeeded => "Company No Longer Needs This Tender",
        TopRejectionReason.UnacceptableOffers => "No Supplier Offers Are Acceptable",
        _ => "Re-Tender requested"
    };
    public static string OutcomeText(FinalDecisionNotice? notice) => notice?.Outcome switch
    {
        FinalDecisionOutcome.Cancel => "Rejected: " + RejectionReasonText(notice.RejectionReason ?? TopRejectionReason.NotNeeded),
        FinalDecisionOutcome.Retender => "Re-Tender requested",
        _ => "Approved"
    };

    // Called from TopVote inside the tender lock, right after the stopping vote and its signature were added.
    private async Task StopTopApprovalOnRejection(Tender t, EvaluationSession s, CommitteeRound round, ApplicationUser actor,
        VoteDecision decision, string comment, int approves, int votes)
    {
        round.Status = CommitteeRoundStatus.Rejected; round.CompletedAt = Now;
        t.Status = TenderStatus.Rejected; // Stops further voting, signing and award until the Officer carries out the outcome.
        var outcome = decision == VoteDecision.Reject ? FinalDecisionOutcome.Cancel : FinalDecisionOutcome.Retender;
        var notice = await db.Set<FinalDecisionNotice>().SingleOrDefaultAsync(x => x.TenderId == t.Id && x.TenderReportId == s.ReportId);
        if (notice != null)
        {
            // The approval threshold was reached earlier but nothing was awarded: the stopping decision replaces that approval.
            Require(t.AwardedBidId == null && notice.Status != FinalDecisionStatus.SuppliersNotified, "This tender was already awarded and announced.");
            Audit(actor, t.Id, "TopApprovalRelayReplacedByRejection", new { notice.Id, PreviousStatus = notice.Status, notice.ManagerAcknowledgedAt, notice.ManagerComment, notice.TopApprovals, notice.TopVotes });
            notice.Status = FinalDecisionStatus.AwaitingManager; notice.ManagerAcknowledgedAt = null; notice.ManagerComment = null;
        }
        else
        {
            notice = new FinalDecisionNotice { TenderId = t.Id, TenderReportId = s.ReportId, CreatedAt = Now, ManagerUserId = await AssignedManagerId(t.Id, s), Status = FinalDecisionStatus.AwaitingManager };
            db.Add(notice);
        }
        notice.Outcome = outcome; notice.RejectionReason = outcome == FinalDecisionOutcome.Cancel ? TopRejectionReason.NotNeeded : null;
        notice.RejectedByUserId = actor.Id; notice.RejectedAt = Now; notice.DecisionReason = comment.Trim();
        notice.TopApprovals = approves; notice.TopVotes = votes;
        var what = outcome == FinalDecisionOutcome.Cancel ? "rejected the tender (the company no longer needs it)" : "requested a Re-Tender";
        if (notice.ManagerUserId != null)
            Notify(notice.ManagerUserId, t.Id, outcome == FinalDecisionOutcome.Cancel ? "Top Committee rejected the tender" : "Top Committee requested a Re-Tender",
                $"A Top Committee member {what}. Reason: {comment.Trim()}. The Top approval is stopped. Review it and forward it to the Sourcing Officer.");
        foreach (var member in (await Members(t.Id, CommitteeType.Top)).Where(x => x.UserId != actor.Id))
            Notify(member.UserId, t.Id, "Top Committee approval stopped", $"A member {what}. No further votes or signatures are accepted for this report.");
        Audit(actor, t.Id, outcome == FinalDecisionOutcome.Cancel ? "TopRejectionStoppedApproval" : "TopRetenderStoppedApproval",
            new { s.ReportId, CommitteeRoundId = round.Id, Decision = decision, Outcome = outcome, Reason = comment.Trim(), Approves = approves, Votes = votes });
    }

    // REJECT: the Officer's single step after the Manager forwarded it (called by NotifySuppliers under the tender lock, at most once).
    private async Task ExecuteRejectionOutcome(Tender t, ApplicationUser officer, FinalDecisionNotice notice)
    {
        Require(notice.Outcome == FinalDecisionOutcome.Cancel, "A Re-Tender request is carried out with \"Prepare the new round\", not with Notify Suppliers.");
        Require(t.Status == TenderStatus.Rejected && t.AwardedBidId == null, "This tender is not waiting for a rejection outcome.");
        var s = await Session(t.Id);
        Require(s.ReportId == notice.TenderReportId, "The rejection belongs to an older report version.");
        var round = await db.TenderRounds.SingleAsync(x => x.Id == s.TenderRoundId);
        var participants = await db.TenderInvitations.Where(x => x.TenderRoundId == round.Id && x.Status != InvitationStatus.Declined).Select(x => x.SupplierId).Distinct().ToListAsync();
        await NotifyParticipants(t, notice, round.Id, participants, "Tender cancelled", "Cancelled",
            "The company no longer needs this tender, so it has been cancelled. No supplier was selected and no further offers are accepted. Thank you for participating.");
        s.State = SessionState.Rejected; s.ClosedAt ??= Now; s.ClosureReason = "Top Committee rejection: the company no longer needs this tender.";
        t.Status = TenderStatus.Cancelled; t.ClosedAt = Now;
        var pr = await db.PurchaseRequests.AsNoTracking().SingleAsync(x => x.Id == t.PurchaseRequestId);
        var departmentUsers = (await users.GetUsersInRoleAsync(SystemRoles.RequestingDepartment)).Where(u => u.IsActive && u.DepartmentId == pr.DepartmentId).Select(u => u.Id)
            .Append(pr.CreatedByUserId).Distinct().ToList();
        foreach (var userId in departmentUsers)
            db.Notifications.Add(new Notification { UserId = userId, Title = "Tender cancelled", CreatedAt = Now, Link = "/Department",
                Message = $"The tender \"{t.Title}\" for your purchase request was cancelled by the Top Committee: the company no longer needs it. Reason: {notice.DecisionReason}" });
        if (notice.ManagerUserId != null) Notify(notice.ManagerUserId, t.Id, "Tender cancelled", "The Sourcing Officer cancelled the tender and notified the suppliers and the requesting department.");
        notice.Status = FinalDecisionStatus.SuppliersNotified; notice.SuppliersNotifiedAt = Now; notice.OfficerUserId = officer.Id;
        Audit(officer, t.Id, "TopRejectionCancellationExecuted", new { notice.Id, notice.TenderReportId, notice.DecisionReason, RoundNumber = round.RoundNumber, Suppliers = participants });
    }

    private async Task NotifyParticipants(Tender t, FinalDecisionNotice notice, int roundId, IEnumerable<int> supplierIds, string title, string outcome, string message, Func<int, string>? perSupplier = null)
    {
        var bids = await db.Bids.Where(b => b.TenderInvitation.TenderRoundId == roundId).Select(b => new { b.Id, b.TenderInvitation.SupplierId }).ToListAsync();
        foreach (var supplierId in supplierIds.Distinct())
        {
            var bid = bids.Where(x => x.SupplierId == supplierId).OrderByDescending(x => x.Id).FirstOrDefault();
            foreach (var userId in await db.Users.Where(u => u.SupplierId == supplierId && u.IsActive).Select(u => u.Id).ToListAsync())
            {
                // Each message concerns only the recipient's own company.
                db.Notifications.Add(new Notification { UserId = userId, Title = title, Message = perSupplier?.Invoke(supplierId) ?? message, CreatedAt = Now,
                    Link = bid != null ? $"/Supplier/Offers/Details/{bid.Id}" : "/Supplier/Workflow" });
                if (bid != null)
                    db.Add(new SupplierResultDelivery { FinalDecisionNotice = notice, TenderId = t.Id, SupplierId = supplierId, BidId = bid.Id, RecipientUserId = userId,
                        Outcome = outcome, CreatedAt = Now, InAppStatus = "Delivered", EmailStatus = "Pending" });
            }
        }
    }

    // RE-TENDER: what the Officer's "Prepare the new round" page shows. Read-only.
    public async Task<RetenderPlan> RetenderPlan(int tenderId, ClaimsPrincipal principal)
    {
        var a = await Actor(principal);
        Require(await IsOfficer(a), "Only the single active Sourcing Officer may prepare a new round.");
        var t = await db.Tenders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenderId);
        Require(t != null, "Tender not found.");
        var notice = await db.Set<FinalDecisionNotice>().AsNoTracking().Where(x => x.TenderId == tenderId).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        var round = await db.TenderRounds.AsNoTracking().Where(x => x.TenderId == tenderId).OrderByDescending(x => x.RoundNumber).FirstAsync();
        var previous = await db.TenderInvitations.AsNoTracking().Where(x => x.TenderRound.TenderId == tenderId && x.Status != InvitationStatus.Declined).Select(x => x.SupplierId).Distinct().ToListAsync();
        var suppliers = await db.Suppliers.AsNoTracking().Where(x => x.Status == SupplierStatus.Approved && db.Users.Any(u => u.SupplierId == x.Id && u.IsActive && u.EmailConfirmed))
            .OrderBy(x => x.CompanyName).Select(x => new RetenderSupplier(x.Id, x.CompanyName, x.Category, false)).ToListAsync();
        string? blocked = notice?.Outcome != FinalDecisionOutcome.Retender ? "There is no Re-Tender request for this tender."
            : notice.Status == FinalDecisionStatus.AwaitingManager ? "The Sourcing Manager has not forwarded the Re-Tender request yet."
            : notice.Status == FinalDecisionStatus.SuppliersNotified ? $"Round {notice.NewRoundNumber} was already published."
            : round.RoundNumber >= MaxRounds ? $"Round {round.RoundNumber} is the last permitted bidding round ({MaxRounds}); a round {MaxRounds + 1} can never be created. Next permissible action: return the decision to the Top Committee through the Sourcing Manager so it can Approve or Reject the existing offers."
            : null;
        return new RetenderPlan
        {
            TenderId = t!.Id, Title = t.Title, Description = t.Description, RoundNumber = round.RoundNumber, MaxRounds = MaxRounds, Blocked = blocked,
            Reason = notice?.DecisionReason, RequestedAt = notice?.RejectedAt, Deadline = DateTime.UtcNow.AddDays(14),
            Suppliers = suppliers.Select(x => x with { Previous = previous.Contains(x.Id) }).OrderByDescending(x => x.Previous).ThenBy(x => x.Name).ToList()
        };
    }

    // RE-TENDER: the Officer publishes the next round of the same tender, after the Manager forwarded the request.
    public Task PublishRetender(int id, RetenderInput input, ClaimsPrincipal principal) => Change(id, principal, "TopRetenderRoundPublished", async (t, a) =>
    {
        await Officer(t, a);
        var notice = await db.Set<FinalDecisionNotice>().Where(x => x.TenderId == id).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        Require(notice?.Outcome == FinalDecisionOutcome.Retender, "There is no Re-Tender request for this tender.");
        Require(notice!.Status != FinalDecisionStatus.SuppliersNotified, $"Round {notice.NewRoundNumber} was already published.");
        Require(notice.Status == FinalDecisionStatus.ForwardedToOfficer, "The Sourcing Manager must review and forward the Re-Tender request first.");
        Require(t.Status == TenderStatus.Rejected && t.AwardedBidId == null, "This tender is not waiting for a Re-Tender.");
        var s = await Session(id);
        Require(s.ReportId == notice.TenderReportId, "The Re-Tender request belongs to an older report version.");
        var round = await db.TenderRounds.SingleAsync(x => x.Id == s.TenderRoundId);
        Require(round.RoundNumber < MaxRounds, $"Re-tendering is blocked: round {round.RoundNumber} is the last permitted bidding round ({MaxRounds}). Round {MaxRounds + 1} can never be created.");
        var title = (input.Title ?? "").Trim(); var description = (input.Description ?? "").Trim();
        Require(title.Length is > 0 and <= 200, "Tender title is required (up to 200 characters).");
        Require(description.Length is > 0 and <= 10000, "Tender description is required (up to 10000 characters).");
        Require(input.Deadline != default && input.Deadline.UtcDateTime > Now.AddMinutes(5), "The new bidding deadline must be in the future.");
        Require(input.Deadline.UtcDateTime <= Now.AddYears(1), "The new bidding deadline must be within one year.");
        var weights = await Weights(id);
        Require(weights.State == WeightState.Approved, "The tender's approved evaluation weights are required for a new round.");
        var supplierIds = (input.SupplierIds ?? []).Distinct().ToList();
        Require(supplierIds.Count >= 3, "Invite at least 3 distinct suppliers to the new round.");
        var eligible = await db.Suppliers.Where(x => supplierIds.Contains(x.Id) && x.Status == SupplierStatus.Approved
            && db.Users.Any(u => u.SupplierId == x.Id && u.IsActive && u.EmailConfirmed)).ToListAsync();
        Require(eligible.Count == supplierIds.Count, "Only approved suppliers with an active verified account can be invited.");

        // Close the rejected cycle; everything stays as history and none of its approvals carry into the new cycle.
        var previous = await db.TenderInvitations.Where(x => x.TenderRoundId == round.Id && x.Status != InvitationStatus.Declined).Select(x => x.SupplierId).Distinct().ToListAsync();
        round.Status = TenderRoundStatus.Failed;
        s.State = SessionState.Rejected; s.ClosedAt ??= Now; s.ClosureReason = "Top Committee Re-Tender: " + notice.DecisionReason;
        db.Add(new RoundDecision { TenderRoundId = round.Id, Kind = RoundDecisionKind.TopRetender, Status = RoundDecisionStatus.Recorded, OfferCount = DistinctSuppliers(await Submitted(round.Id)),
            Justification = ("Top Committee Re-Tender: " + notice.DecisionReason)[..Math.Min(2000, ("Top Committee Re-Tender: " + notice.DecisionReason).Length)],
            RequestedByUserId = notice.RejectedByUserId ?? a.Id, RequestedAt = notice.RejectedAt ?? Now, DecidedByUserId = a.Id, DecidedAt = Now });
        Audit(a, id, "TenderDetailsEditedForRetender", new { OldTitle = t.Title, NewTitle = title, OldDescription = t.Description, NewDescription = description });
        t.Title = title; t.Description = description; t.Status = TenderStatus.Bidding;
        var next = new TenderRound { TenderId = id, RoundNumber = round.RoundNumber + 1, Status = TenderRoundStatus.Open, OpensAt = Now, Deadline = input.Deadline.UtcDateTime, CreatedByUserId = a.Id, CreatedAt = Now };
        db.TenderRounds.Add(next);
        await db.SaveChangesAsync();
        foreach (var supplier in eligible)
        {
            db.TenderInvitations.Add(new TenderInvitation { TenderRoundId = next.Id, SupplierId = supplier.Id, InvitedAt = Now, InvitedByUserId = a.Id });
            await QueueSupplierEmail(next, supplier, a);
        }
        // Previous participants hear that the tender is reissued (with or without a new invitation); new suppliers get their invitation.
        await NotifyParticipants(t, notice, round.Id, previous, "Tender reissued", "Reissued", "", supplierId => supplierIds.Contains(supplierId)
            ? $"This tender was reissued as round {next.RoundNumber}. You are invited again: accept the invitation and submit a NEW offer before {(next.Deadline.HasValue ? JordanTime.Display(next.Deadline.Value) : "the deadline")}. Your earlier offer stays on record but is not evaluated."
            : $"This tender was reissued as round {next.RoundNumber}. Your company was not invited to the new round. Your earlier offer stays on record.");
        foreach (var supplierId in supplierIds.Except(previous))
            foreach (var userId in await db.Users.Where(u => u.SupplierId == supplierId && u.IsActive).Select(u => u.Id).ToListAsync())
                Notify(userId, id, "Tender invitation", $"You are invited to round {next.RoundNumber} of this tender. Review the requirements, accept or decline, and submit your offer before {(next.Deadline.HasValue ? JordanTime.Display(next.Deadline.Value) : "the deadline")}.");
        notice.Status = FinalDecisionStatus.SuppliersNotified; notice.SuppliersNotifiedAt = Now; notice.OfficerUserId = a.Id; notice.NewRoundNumber = next.RoundNumber;
        if (notice.ManagerUserId != null) Notify(notice.ManagerUserId, id, "Tender reissued", $"Round {next.RoundNumber} of {MaxRounds} is open with {supplierIds.Count} invited suppliers. Compliance, AI review, evaluation and approvals start again.");
        Audit(a, id, "TopRetenderExecuted", new { notice.Id, FailedRound = round.RoundNumber, NewRound = next.RoundNumber, next.Deadline, Invited = supplierIds, Previous = previous });
    });
}
