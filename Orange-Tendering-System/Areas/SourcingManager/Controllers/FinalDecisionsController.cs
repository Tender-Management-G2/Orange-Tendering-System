using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.SourcingManager.Controllers;

// Step 2 of the final decision relay: the Sourcing Manager assigned to the tender acknowledges the Top Committee decision and forwards it to the Officer.
[Area("SourcingManager"), Authorize(Roles = SystemRoles.SourcingManager), AutoValidateAntiforgeryToken]
public sealed class FinalDecisionsController(TenderWorkflowService workflow) : Controller
{
    public async Task<IActionResult> Index() => View(await workflow.FinalDecisions(User));

    [HttpGet] public async Task<IActionResult> Acknowledge(int id)
    {
        var item = (await workflow.FinalDecisions(User)).FirstOrDefault(x => x.TenderId == id);
        return item == null ? NotFound() : View(item);
    }

    [HttpPost]
    public async Task<IActionResult> Acknowledge(int id, string? comment)
    {
        var ajax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        try { await workflow.ManagerAcknowledge(id, comment, User); TempData["Flash"] = "Decision acknowledged and forwarded to the Sourcing Officer."; if (ajax) return Json(new { ok = true }); }
        catch (WorkflowException e) { if (ajax) return BadRequest(new { error = e.Message }); TempData["FlashError"] = e.Message; }
        return RedirectToAction(nameof(Index));
    }
}
