using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

// Sourcing Officer dashboard. Read-only: every number and row is a SQL Server query; no workflow state is changed here.
[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
public class DashboardController(ApplicationDbContext db) : Controller
{
    private static readonly TenderStatus[] InProgress =
        [TenderStatus.Bidding, TenderStatus.Evaluation, TenderStatus.OfficerReview, TenderStatus.ManagerReview, TenderStatus.TopCommitteeReview];
    private static readonly TenderStatus[] EvaluationStages =
        [TenderStatus.Evaluation, TenderStatus.OfficerReview, TenderStatus.ManagerReview, TenderStatus.TopCommitteeReview];

    public async Task<IActionResult> Index()
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var me = await db.Users.AsNoTracking().Where(u => u.Id == officerId).Select(u => u.FullName).SingleOrDefaultAsync();

        // A tender is "re-tendering" when its latest round followed a failed round and bidding has not reopened yet.
        var retendering = db.Tenders.AsNoTracking().Where(t => t.Status == TenderStatus.Draft && t.Rounds.Any(r => r.Status == TenderRoundStatus.Failed));
        var active = db.Tenders.AsNoTracking().Where(t => InProgress.Contains(t.Status) || retendering.Any(x => x.Id == t.Id));

        var handoffs = await Handoffs();

        // Same work items as the My Tasks page, plus the Manager handoffs shown on this dashboard.
        var actionItems = await db.PurchaseRequests.CountAsync(p => p.Status == PurchaseRequestStatus.Submitted)
            + await db.Tenders.CountAsync(t => t.Status == TenderStatus.OfficerReview)
            + await db.Clarifications.CountAsync(c => c.ToUserId == officerId && c.Status == ClarificationStatus.Open)
            + handoffs.Count;

        var invited = await db.TenderInvitations.AsNoTracking()
            .Where(i => InProgress.Contains(i.TenderRound.Tender.Status) && i.TenderRound.Status != TenderRoundStatus.Failed)
            .Select(i => i.SupplierId).Distinct().CountAsync();

        var statusRows = await db.Tenders.AsNoTracking().GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        int Count(params TenderStatus[] s) => statusRows.Where(r => s.Contains(r.Key)).Sum(r => r.Count);

        var activeRows = await active
            .Select(t => new
            {
                t.Id, t.Title, t.Status, t.CreatedAt,
                Round = t.Rounds.OrderByDescending(r => r.RoundNumber).Select(r => new { r.RoundNumber, r.Deadline, r.Status }).FirstOrDefault(),
                Failed = t.Rounds.Any(r => r.Status == TenderRoundStatus.Failed)
            })
            .ToListAsync();
        var summaries = activeRows
            .Select(t => new OfficerTenderSummary(t.Id, t.Title, t.Status, t.Round?.RoundNumber ?? 1,
                t.Round?.Status == TenderRoundStatus.Open ? t.Round.Deadline : null, t.Status == TenderStatus.Draft && t.Failed))
            .ToList();

        var focus = activeRows.OrderByDescending(t => t.CreatedAt).Select(t => summaries.First(s => s.Id == t.Id)).FirstOrDefault();
        var list = summaries
            .OrderBy(t => t.Deadline.HasValue && t.Deadline > DateTime.UtcNow ? 0 : 1).ThenBy(t => t.Deadline)
            .ThenByDescending(t => activeRows.First(r => r.Id == t.Id).CreatedAt)
            .Take(3).ToList();

        var activities = await db.Notifications.AsNoTracking().Where(n => n.UserId == officerId)
            .OrderByDescending(n => n.CreatedAt).Take(3)
            .Select(n => new OfficerActivity(n.Title, n.Message, n.CreatedAt, n.Link)).ToListAsync();

        var model = new SourcingOfficerDashboardViewModel
        {
            DisplayName = me,
            ActionItems = actionItems,
            ActiveTenders = summaries.Count,
            InvitedSuppliers = invited,
            OngoingEvaluations = Count(TenderStatus.Evaluation, TenderStatus.OfficerReview),
            Handoffs = handoffs,
            FocusTender = focus,
            ActiveTenderList = list,
            StatusSlices =
            [
                new("bidding", Count(TenderStatus.Bidding)),
                new("evaluation", Count(EvaluationStages)),
                new("awarded", Count(TenderStatus.Awarded)),
                new("retendering", summaries.Count(s => s.Retendering))
            ],
            Activities = activities
        };
        return View(model);
    }

    // Real Manager -> Officer handoffs only:
    //  - the assigned Sourcing Manager rejected a "proceed with fewer suppliers" request, so the Officer may re-tender the round;
    //  - the Manager approved that request, so the Officer can schedule the evaluation;
    //  - the Manager acknowledged and forwarded a final decision to the Officer.
    // Each item disappears once the Officer has acted (new round, evaluation scheduled, suppliers notified).
    private async Task<List<OfficerHandoff>> Handoffs()
    {
        var decisions = await db.Set<RoundDecision>().AsNoTracking()
            .Where(d => d.Kind == RoundDecisionKind.ProceedException
                && (d.Status == RoundDecisionStatus.Rejected || d.Status == RoundDecisionStatus.Approved)
                && d.TenderRound.Status == TenderRoundStatus.Closed
                && !d.TenderRound.Tender.Rounds.Any(r => r.RoundNumber > d.TenderRound.RoundNumber)
                && !db.Set<EvaluationSession>().Any(s => s.TenderRoundId == d.TenderRoundId))
            .Select(d => new { d.Status, d.TenderRound.TenderId, d.TenderRound.Tender.Title, d.TenderRound.RoundNumber, d.DecisionComment, d.DecidedAt, d.RequestedAt })
            .ToListAsync();
        var items = decisions
            .Where(d => d.Status == RoundDecisionStatus.Approved || d.RoundNumber < TenderWorkflowService.MaxRounds)
            .Select(d => new OfficerHandoff(d.Status == RoundDecisionStatus.Rejected ? OfficerHandoffKind.Retender : OfficerHandoffKind.ProceedApproved,
                d.TenderId, d.Title, d.DecisionComment, d.RoundNumber, TenderWorkflowService.MaxRounds, d.DecidedAt ?? d.RequestedAt))
            .ToList();

        var notices = await db.FinalDecisionNotices.AsNoTracking()
            .Where(n => n.Status == FinalDecisionStatus.ForwardedToOfficer)
            .Select(n => new { n.TenderId, n.Tender.Title, n.ManagerComment, n.ManagerAcknowledgedAt, n.CreatedAt,
                Round = n.Tender.Rounds.Max(r => (int?)r.RoundNumber) ?? 1 })
            .ToListAsync();
        items.AddRange(notices.Select(n => new OfficerHandoff(OfficerHandoffKind.FinalDecision, n.TenderId, n.Title, n.ManagerComment, n.Round,
            TenderWorkflowService.MaxRounds, n.ManagerAcknowledgedAt ?? n.CreatedAt)));

        return items.OrderBy(i => i.Kind).ThenByDescending(i => i.At).ToList();
    }
}
