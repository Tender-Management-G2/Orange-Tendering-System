using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers;

// Shared notification/navigation entry point only; business endpoints live in their Areas.
[Authorize]
public class WorkflowController(TenderWorkflowService workflow) : Controller
{
    private async Task<(string Area, string Controller)> Destination()
    {
        var actor = await workflow.Actor(User);
        foreach (var (role, area) in new[] { (SystemRoles.Supplier, "Supplier"), (SystemRoles.SourcingOfficer, "SourcingOfficer"), (SystemRoles.SourcingManager, "SourcingManager"), (SystemRoles.EvaluationCommittee, "EvaluationCommittee"), (SystemRoles.TopCommittee, "TopCommittee") })
            if (await workflow.Role(actor, role)) return (area, "Workflow");
        return ("EvaluationCommittee", "WeightApprovals");
    }
    public async Task<IActionResult> Index() { var d = await Destination(); return RedirectToAction("Index", d.Controller, new { area = d.Area }); }
    public async Task<IActionResult> Details(int id) { var d = await Destination(); return RedirectToAction("Details", d.Controller, new { area = d.Area, id }); }
}
