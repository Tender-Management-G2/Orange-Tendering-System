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
        return View(items);
    }

    [HttpPost]
    public async Task<IActionResult> Notify(int id)
    {
        var ajax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        try { await decisions.NotifySuppliers(id, User); TempData["Flash"] = "Suppliers were notified of the final result."; if (ajax) return Json(new { ok = true }); }
        catch (WorkflowException e) { if (ajax) return BadRequest(new { error = e.Message }); TempData["FlashError"] = e.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Retry(int id)
    {
        try { await decisions.RetryEmails(id, User); TempData["Flash"] = "Failed e-mail deliveries were retried."; }
        catch (WorkflowException e) { TempData["FlashError"] = e.Message; }
        return RedirectToAction(nameof(Index));
    }
}
