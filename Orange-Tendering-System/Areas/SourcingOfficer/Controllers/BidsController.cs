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
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
public class BidsController(ApplicationDbContext db, TenderWorkflowService workflow) : Controller
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
        var tenderId = await db.Bids.Where(b => b.Id == id && b.Status == BidStatus.Submitted)
            .Select(b => (int?)b.TenderInvitation.TenderRound.TenderId).SingleOrDefaultAsync();
        if (!tenderId.HasValue) return NotFound();
        try
        {
            await workflow.Compliance(tenderId.Value, id, status,
                !string.IsNullOrWhiteSpace(notes) ? notes.Trim() : (status == ComplianceStatus.Compliant ? "Mandatory requirements reviewed; previous missing requirements resolved." : missingRequirements ?? ""),
                missingRequirements?.Trim() ?? "", User);
        }
        catch (WorkflowException error)
        {
            ModelState.AddModelError("", error.Message);
            var result = await Compliance(id);
            if (result is ViewResult { Model: Bid model })
            {
                model.ComplianceStatus = status;
                model.ComplianceNotes = notes;
                model.MissingRequirements = missingRequirements;
            }
            return result;
        }
        TempData["ComplianceSaved"] = "Compliance decision saved.";
        return RedirectToAction(nameof(Index), new { id = tenderId.Value });
    }

    public async Task<IActionResult> ForwardToCommittee(int id)
    {
        var tenderId = await db.TenderRounds.Where(r => r.Id == id).Select(r => (int?)r.TenderId).SingleOrDefaultAsync();
        return tenderId.HasValue ? RedirectToAction("Details", "Workflow", new { id = tenderId.Value }) : NotFound();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult ForwardToCommittee(OfficerReportActionModel model) => StatusCode(StatusCodes.Status409Conflict,
        "Continue from the Tender page. Closing bidding, screening offers and scheduling the committee use the same authorized workflow.");
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
