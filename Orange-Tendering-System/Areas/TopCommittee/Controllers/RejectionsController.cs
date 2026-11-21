using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = SystemRoles.TopCommittee), AutoValidateAntiforgeryToken]
public class RejectionsController(TenderWorkflowService workflow) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        try { return View(await workflow.TopRejectionDetails(id, User)); }
        catch (WorkflowException e) { return BadRequest(e.Message); }
    }

    [HttpPost]
    public async Task<IActionResult> Agree(int id, TopRejectionReason? reason, string comment, string documentHash, bool confirmed)
    {
        if (!ModelState.IsValid || !reason.HasValue) return BadRequest("Invalid reason agreement.");
        try
        {
            await workflow.AgreeTopRejectionReason(id, reason.Value, comment, documentHash, confirmed, User);
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (WorkflowException e) { return BadRequest(e.Message); }
        catch (DbUpdateException) { return Conflict("The decision changed. Reload the report before retrying."); }
    }
}
