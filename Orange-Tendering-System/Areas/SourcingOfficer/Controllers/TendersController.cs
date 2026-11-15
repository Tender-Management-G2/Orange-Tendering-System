using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

// Compatibility addresses plus re-tendering. Tender creation and other mutations use WorkflowController and its service.
[Area("SourcingOfficer"), Authorize(Roles = SystemRoles.SourcingOfficer)]
public class TendersController(ApplicationDbContext db, TenderWorkflowService workflow) : Controller
{
    [HttpGet] public IActionResult Index() => RedirectToAction("Index", "Workflow");
    [HttpGet] public IActionResult Create(int id) => RedirectToAction("Index", "Workflow");
    [HttpGet] public IActionResult Details(int id) => RedirectToAction("Details", "Workflow", new { id });
    // Re-tendering (Tariq-final): round history and the next round go through the shared, guarded Retender service.
    [HttpGet]
    public async Task<IActionResult> Rounds(int id)
    {
        var tender = await db.Tenders.AsNoTracking()
            .Include(t => t.Rounds).ThenInclude(r => r.Invitations).ThenInclude(i => i.Bids)
            .SingleOrDefaultAsync(t => t.Id == id);
        if (tender == null) return NotFound();

        var orderedRounds = tender.Rounds.OrderBy(r => r.RoundNumber).ToList();
        var latest = orderedRounds.LastOrDefault();
        var latestSuppliers = latest?.Invitations.Where(i => i.Bids.Any(b => b.Status == BidStatus.Submitted))
            .Select(i => i.SupplierId).Distinct().Count() ?? 0;
        var canStart = latest != null && latest.Status == TenderRoundStatus.Closed
            && latest.RoundNumber < TenderWorkflowService.MaxRounds;

        var model = new TenderRoundsViewModel
        {
            TenderId = tender.Id,
            TenderTitle = tender.Title,
            CanStartNextRound = canStart,
            NextRoundMessage = canStart ? null : latest == null ? "This tender has no rounds yet."
                : latest.RoundNumber >= TenderWorkflowService.MaxRounds ? "Four bidding rounds is the maximum. No fifth round is permitted."
                : "Close bidding through the tender workflow before re-tendering. Further evaluation safeguards are checked by the shared service.",
            Rounds = orderedRounds.Select(r =>
            {
                var submitted = r.Invitations.SelectMany(i => i.Bids).Where(b => b.Status == BidStatus.Submitted).ToList();
                var suppliers = r.Invitations.Where(i => i.Bids.Any(b => b.Status == BidStatus.Submitted))
                    .Select(i => i.SupplierId).Distinct().Count();
                return new TenderRoundRowViewModel(r.Id, r.RoundNumber, r.Status, r.OpensAt, r.Deadline,
                    r.Invitations.Count, submitted.Count, suppliers);
            }).ToList()
        };
        return View(model);
    }

    [HttpGet] public IActionResult Edit(int id) => RedirectToAction("Details", "Workflow", new { id });
    [HttpGet] public IActionResult Timeline(int id) => RedirectToAction("Details", "Workflow", new { id });
    [HttpGet] public IActionResult Committees(int id) => RedirectToAction("Details", "Workflow", new { id });
    [HttpGet]
    public async Task<IActionResult> SelectSuppliers(int id, string? category = null)
    {
        var tenderId = await db.TenderRounds.Where(r => r.Id == id).Select(r => (int?)r.TenderId).SingleOrDefaultAsync();
        return tenderId.HasValue ? RedirectToAction("Details", "Workflow", new { id = tenderId.Value }) : NotFound();
    }
    // Old POST forms cannot bypass committee assignment, approved weights, deadlines or duplicate protection.
    [HttpPost, ValidateAntiForgeryToken] public IActionResult Create(CreateOfficerTenderModel model) => UseTenderPage();
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> StartNextRound(int tenderId, string reason)
    {
        if (!ModelState.IsValid) return BadRequest("Supply a valid re-tender reason.");
        try
        {
            // The shared service owns authorization, tender-row locking, round limits,
            // cancellation policy and audit history. Bidding is reopened separately with
            // a fresh deadline, which invokes the shared supplier carry-forward rules.
            await workflow.Retender(tenderId, reason, User);
        }
        catch (WorkflowException error) { return BadRequest(error.Message); }
        catch (DbUpdateException) { return Conflict("The round changed. Reload before trying again."); }
        TempData.Orange(OrangeFeedback.Popup("New round started!", "Open it through the tender workflow with a fresh deadline; eligible suppliers are then invited automatically."));
        return RedirectToAction("Index", "Workflow", new { open = tenderId });
    }
    [HttpPost, ValidateAntiForgeryToken] public IActionResult InviteSupplier(int roundId, int supplierId, string category) => UseTenderPage();
    private IActionResult UseTenderPage() => StatusCode(StatusCodes.Status409Conflict,
        "This form has moved. Open the Tender page to continue the authorized workflow.");
}
