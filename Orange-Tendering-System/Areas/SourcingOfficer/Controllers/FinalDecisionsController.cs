using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

// Step 3: only the single Sourcing Officer may announce results to suppliers ("Notify Suppliers"), once, and retry failed e-mails.
[Area("SourcingOfficer"), Authorize(Roles = SystemRoles.SourcingOfficer), AutoValidateAntiforgeryToken]
public sealed class FinalDecisionsController(TenderWorkflowService workflow, FinalDecisionService decisions, ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        List<FinalDecisionNotice> items;
        // The server rule (exactly one active Sourcing Officer) can refuse the list; show that as a message, not a 500 error page.
        try { items = await workflow.FinalDecisions(User); }
        catch (WorkflowException e) { items = []; TempData["FlashError"] = e.Message; }
        ViewBag.Suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.CompanyName);
        ViewBag.Emails = await db.Users.AsNoTracking().Where(u => u.SupplierId != null).ToDictionaryAsync(u => u.Id, u => u.Email);
        var tenderIds = items.Select(x => x.TenderId).Distinct().ToList();
        ViewBag.Rounds = await db.TenderRounds.AsNoTracking().Where(r => tenderIds.Contains(r.TenderId)).GroupBy(r => r.TenderId)
            .Select(g => new { g.Key, Max = g.Max(r => r.RoundNumber) }).ToDictionaryAsync(x => x.Key, x => x.Max);
        return View(items);
    }

    [HttpPost]
    public async Task<IActionResult> Notify(int id)
    {
        var ajax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        try
        {
            await decisions.NotifySuppliers(id, User);
            var notified = OrangeFeedback.Popup("Suppliers notified!", "Every participating supplier was told the outcome.", OrangeVariant.Send);
            if (ajax) return Json(new { ok = true, feedback = notified.ToJsonElement() });
            TempData.Orange(notified);
        }
        catch (WorkflowException e) { if (ajax) return BadRequest(new { error = e.Message }); TempData["FlashError"] = e.Message; }
        return RedirectToAction(nameof(Index));
    }

    // Re-Tender requested by the Top Committee and forwarded by the Manager: edit permitted details, deadline and invitations, then publish.
    [HttpGet]
    public async Task<IActionResult> Retender(int id)
    {
        try { return View(await workflow.RetenderPlan(id, User)); }
        catch (WorkflowException e) { TempData["FlashError"] = e.Message; return RedirectToAction(nameof(Index)); }
    }

    [HttpPost]
    public async Task<IActionResult> Retender(int id, Orange_Tendering_System.ViewModels.RetenderInput input)
    {
        var ajax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        if (ModelState.IsValid)
        {
            try
            {
                await workflow.PublishRetender(id, input, User);
                var published = OrangeFeedback.Popup("New round published", "Previous and newly invited suppliers were notified. They must submit new offers.", OrangeVariant.Send);
                if (ajax) return Json(new { ok = true, feedback = published.ToJsonElement() });
                TempData.Orange(published);
                return RedirectToAction("Index", "Workflow", new { open = id });
            }
            catch (WorkflowException e) { ModelState.AddModelError("", e.Message); }
        }
        if (ajax) return BadRequest(new { error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Check the form." });
        // Re-render with the entered values kept.
        var plan = await workflow.RetenderPlan(id, User);
        plan.Title = input.Title ?? ""; plan.Description = input.Description ?? "";
        if (input.Deadline != default) plan.Deadline = input.Deadline.UtcDateTime;
        ViewBag.Selected = input.SupplierIds ?? [];
        return View(plan);
    }

    [HttpPost]
    public async Task<IActionResult> Retry(int id)
    {
        try { await decisions.RetryEmails(id, User); TempData.Orange(OrangeFeedback.Toast("Deliveries retried", "Failed e-mails were sent again.", OrangeVariant.Send)); }
        catch (WorkflowException e) { TempData["FlashError"] = e.Message; }
        return RedirectToAction(nameof(Index));
    }
}
