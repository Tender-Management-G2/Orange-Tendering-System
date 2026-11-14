// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Saja
// Module: Sourcing Officer / Bid Management
// Purpose: MVC endpoints for the Bids pages of the Sourcing Officer workspace.
// Compatibility GET routes: use the persisted, authorized weighted workflow.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
public class BidsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(int? id)
    {
        var offers = await db.Bids.AsNoTracking()
            .Include(b => b.TenderInvitation).ThenInclude(i => i.Supplier)
            .Include(b => b.TenderInvitation).ThenInclude(i => i.TenderRound).ThenInclude(r => r.Tender)
            .Where(b => b.Status == BidStatus.Submitted && (!id.HasValue || b.TenderInvitation.TenderRound.TenderId == id))
            .OrderByDescending(b => b.SubmittedAt).ToListAsync();
        return View(new SourcingOfficerBidsViewModel
        {
            Offers = offers,
            Rounds = await GetRoundSummariesAsync(id)
        });
    }

    public async Task<IActionResult> Forwarding() => View(new SourcingOfficerBidsViewModel
    {
        Rounds = await GetRoundSummariesAsync()
    });

    private async Task<List<OfferRoundSummaryViewModel>> GetRoundSummariesAsync(int? tenderId = null)
    {
        var roundIds = await db.Bids.AsNoTracking()
            .Where(b => b.Status == BidStatus.Submitted
                && (!tenderId.HasValue || b.TenderInvitation.TenderRound.TenderId == tenderId))
            .Select(b => b.TenderInvitation.TenderRoundId).Distinct().ToListAsync();
        var rounds = await db.TenderRounds.AsNoTracking().Where(r => roundIds.Contains(r.Id))
            .Select(r => new { r.Id, r.TenderId, r.RoundNumber, TenderTitle = r.Tender.Title, r.Status }).ToListAsync();
        var compliantCounts = await db.Bids.AsNoTracking()
            .Where(b => roundIds.Contains(b.TenderInvitation.TenderRoundId)
                && b.Status == BidStatus.Submitted && b.ComplianceStatus == ComplianceStatus.Compliant)
            .GroupBy(b => b.TenderInvitation.TenderRoundId)
            .Select(g => new { RoundId = g.Key, Count = g.Select(b => b.TenderInvitation.SupplierId).Distinct().Count() })
            .ToDictionaryAsync(x => x.RoundId, x => x.Count);
        var tenderIds = rounds.Select(r => r.TenderId).Distinct().ToList();
        var memberCounts = await db.CommitteeMembers.AsNoTracking()
            .Where(m => tenderIds.Contains(m.TenderId) && m.CommitteeType == CommitteeType.Evaluation)
            .GroupBy(m => m.TenderId).Select(g => new { TenderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenderId, x => x.Count);

        var summaries = new List<OfferRoundSummaryViewModel>();
        foreach (var round in rounds)
        {
            var failedAttempts = await CountFailedAttemptsAsync(round.TenderId, round.RoundNumber);
            summaries.Add(new OfferRoundSummaryViewModel(round.Id, round.TenderId, round.RoundNumber,
                round.TenderTitle, round.Status, compliantCounts.GetValueOrDefault(round.Id),
                memberCounts.GetValueOrDefault(round.TenderId), failedAttempts >= 2 ? 2 : 3, failedAttempts));
        }
        return summaries;
    }

    public async Task<IActionResult> Details(int id)
    {
        var bid = await db.Bids.AsNoTracking().Include(b => b.TenderInvitation).ThenInclude(i => i.Supplier).Include(b => b.TenderInvitation.TenderRound).ThenInclude(r => r.Tender).Include(b => b.Items).ThenInclude(i => i.TenderItem).Include(b => b.Documents).SingleOrDefaultAsync(b => b.Id == id);
        return bid == null ? NotFound() : View(bid);
    }

    public async Task<IActionResult> Compliance(int id)
    {
        var bid = await db.Bids.AsNoTracking()
            .Include(b => b.TenderInvitation).ThenInclude(i => i.Supplier)
            .Include(b => b.TenderInvitation).ThenInclude(i => i.TenderRound).ThenInclude(r => r.Tender)
            .Include(b => b.Documents)
            .SingleOrDefaultAsync(b => b.Id == id && b.Status == BidStatus.Submitted);
        return bid == null ? NotFound() : View(bid);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Compliance(int id, ComplianceStatus status, string? notes, string? missingRequirements)
    {
        var bid = await db.Bids.SingleOrDefaultAsync(b => b.Id == id && b.Status == BidStatus.Submitted);
        if (bid == null) return NotFound();
        if (status is not (ComplianceStatus.Compliant or ComplianceStatus.NonCompliant or ComplianceStatus.PendingClarification)) return BadRequest();
        if (status == ComplianceStatus.NonCompliant && string.IsNullOrWhiteSpace(notes))
            ModelState.AddModelError(nameof(notes), "Explain why this offer does not meet the requirements.");
        if (status == ComplianceStatus.PendingClarification && string.IsNullOrWhiteSpace(missingRequirements))
            ModelState.AddModelError(nameof(missingRequirements), "List the information or documents you need from the supplier.");
        if (!ModelState.IsValid)
        {
            var model = await db.Bids.AsNoTracking()
                .Include(b => b.TenderInvitation).ThenInclude(i => i.Supplier)
                .Include(b => b.TenderInvitation).ThenInclude(i => i.TenderRound).ThenInclude(r => r.Tender)
                .Include(b => b.Documents).SingleOrDefaultAsync(b => b.Id == id);
            if (model == null) return NotFound();
            model.ComplianceStatus = status;
            model.ComplianceNotes = notes;
            model.MissingRequirements = missingRequirements;
            return View("Compliance", model);
        }
        bid.ComplianceStatus = status;
        bid.ComplianceNotes = status == ComplianceStatus.NonCompliant ? notes?.Trim() : null;
        bid.MissingRequirements = status == ComplianceStatus.PendingClarification ? missingRequirements?.Trim() : null;
        bid.ComplianceCheckedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
        bid.ComplianceCheckedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var tenderId = await db.TenderInvitations.Where(i => i.Id == bid.TenderInvitationId)
            .Select(i => i.TenderRound.TenderId).SingleAsync();
        TempData["ComplianceSaved"] = "Compliance decision saved.";
        return RedirectToAction(nameof(Index), new { id = tenderId });
    }

    public async Task<IActionResult> ForwardToCommittee(int id)
    {
        var round = await db.TenderRounds.AsNoTracking().Include(r => r.Tender).SingleOrDefaultAsync(r => r.Id == id);
        if (round == null) return NotFound();
        var bids = await db.Bids.AsNoTracking().Include(b => b.TenderInvitation).ThenInclude(i => i.Supplier).Include(b => b.Items)
            .Where(b => b.TenderInvitation.TenderRoundId == id && b.Status == BidStatus.Submitted && b.ComplianceStatus == ComplianceStatus.Compliant).OrderBy(b => b.Id).ToListAsync();
        var failedAttempts = await CountFailedAttemptsAsync(round.TenderId, round.RoundNumber);
        return View(new OfficerReportActionModel
        {
            RoundId = id, Bids = bids, PreviousFailedAttempts = failedAttempts,
            MinimumRequiredOffers = failedAttempts >= 2 ? 2 : 3
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForwardToCommittee(OfficerReportActionModel model)
    {
        var round = await db.TenderRounds.Include(r => r.Tender).SingleOrDefaultAsync(r => r.Id == model.RoundId);
        if (round == null) return NotFound();
        if (round.Status != TenderRoundStatus.Open) return BadRequest();
        var failedAttempts = await CountFailedAttemptsAsync(round.TenderId, round.RoundNumber);
        var minimumOffers = failedAttempts >= 2 ? 2 : 3;
        var selectedIds = model.BidIds?.Distinct().ToList() ?? [];
        var allEligible = await db.Bids.Include(b => b.TenderInvitation).ThenInclude(i => i.Supplier)
            .Include(b => b.Items)
            .Where(b => b.TenderInvitation.TenderRoundId == round.Id && b.Status == BidStatus.Submitted
                && b.ComplianceStatus == ComplianceStatus.Compliant)
            .OrderBy(b => b.Id).ToListAsync();
        var eligible = allEligible.Where(b => selectedIds.Contains(b.Id)).ToList();
        if (eligible.Count == 0 || eligible.Select(b => b.TenderInvitation.SupplierId).Distinct().Count() != eligible.Count)
            ModelState.AddModelError(nameof(model.BidIds), "Select compliant offers from different suppliers.");
        if (eligible.Select(b => b.TenderInvitation.SupplierId).Distinct().Count() < minimumOffers)
            ModelState.AddModelError(nameof(model.BidIds), $"Select at least {minimumOffers} compliant offers from different suppliers. The minimum is reduced to 2 only after 2 previous failed attempts.");
        if (await db.CommitteeMembers.CountAsync(m => m.TenderId == round.TenderId && m.CommitteeType == CommitteeType.Evaluation) != 4) ModelState.AddModelError(nameof(model.BidIds), "Assign all four evaluation committee members first.");
        if (!ModelState.IsValid) { model.Bids = allEligible; model.BidIds = selectedIds; model.MinimumRequiredOffers = minimumOffers; model.PreviousFailedAttempts = failedAttempts; return View(model); }
        var now = DateTime.UtcNow;
        var nextVersion = (await db.TenderReports.Where(r => r.TenderRoundId == round.Id && r.ReportType == ReportType.OfficerComparison).Select(r => (int?)r.VersionNumber).MaxAsync() ?? 0) + 1;
        var report = new TenderReport { TenderRoundId = round.Id, ReportType = ReportType.OfficerComparison, VersionNumber = nextVersion, Content = model.Comment?.Trim() ?? "Officer compliance review and selected offers.", RankingSnapshotJson = System.Text.Json.JsonSerializer.Serialize(eligible.Select(b => new { b.Id, b.ComplianceStatus, Supplier = b.TenderInvitation.SupplierId })), Status = ReportStatus.Submitted, CreatedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value, CreatedAt = now, SubmittedAt = now };
        db.TenderReports.Add(report); await db.SaveChangesAsync();
        db.CommitteeRounds.Add(new CommitteeRound { TenderRoundId = round.Id, CommitteeType = CommitteeType.Evaluation, RoundNumber = (await db.CommitteeRounds.Where(c => c.TenderRoundId == round.Id && c.CommitteeType == CommitteeType.Evaluation).Select(c => (int?)c.RoundNumber).MaxAsync() ?? 0) + 1, TenderReportId = report.Id, Status = CommitteeRoundStatus.InProgress, StartedAt = now });
        round.Status = TenderRoundStatus.SentToEvaluation; round.Tender.Status = TenderStatus.Evaluation;
        var committeeMembers = await db.CommitteeMembers.Where(m => m.TenderId == round.TenderId && m.CommitteeType == CommitteeType.Evaluation).ToListAsync();
        foreach (var member in committeeMembers)
            db.Notifications.Add(new Notification { UserId = member.UserId, Title = "Offers ready for evaluation", Message = $"The Sourcing Officer sent offers for {round.Tender.Title} (round {round.RoundNumber}) to the Evaluation Committee.", Link = "/EvaluationCommittee/Evaluations", CreatedAt = now });
        await db.SaveChangesAsync();
        TempData["CommitteeForwarded"] = "Selected compliant offers were sent to the Technical & Financial Evaluation Committee.";
        return RedirectToAction("Index", "Reports");
    }

    public IActionResult History(int? id) => RedirectToAction("Index", "Workflow");

    private async Task<int> CountFailedAttemptsAsync(int tenderId, int beforeRoundNumber)
    {
        var previousRounds = await db.TenderRounds.AsNoTracking()
            .Where(r => r.TenderId == tenderId && r.RoundNumber < beforeRoundNumber
                && r.Status == TenderRoundStatus.Failed)
            .Include(r => r.Invitations).ThenInclude(i => i.Bids)
            .ToListAsync();

        return previousRounds.Count(r => r.Invitations.Count > 0
            && r.Invitations.Where(i => i.Bids.Any(b => b.Status == BidStatus.Submitted))
                .Select(i => i.SupplierId).Distinct().Count() < 3);
    }
}
