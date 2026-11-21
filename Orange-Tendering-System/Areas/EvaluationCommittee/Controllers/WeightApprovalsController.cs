using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.EvaluationCommittee.Controllers;

// Legacy links redirect to the role's workflow; only the assigned Sourcing Officer can approve weights there.
[Area("EvaluationCommittee"), Authorize]
public class WeightApprovalsController(TenderWorkflowService workflow, ProtectedDocumentStore documents) : WorkflowControllerBase(workflow, documents)
{
    public override Task<IActionResult> Index() => Task.FromResult<IActionResult>(RedirectToAction("Index", "Workflow", new { area = "" }));
    public override Task<IActionResult> Details(int id) => Task.FromResult<IActionResult>(RedirectToAction("Details", "Workflow", new { area = "", id }));
    [HttpPost] public IActionResult Approve(int id) => StatusCode(StatusCodes.Status409Conflict,
        "Weight approval moved to the assigned Sourcing Officer's workflow page.");
}
