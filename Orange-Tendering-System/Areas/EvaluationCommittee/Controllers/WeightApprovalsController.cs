using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.EvaluationCommittee.Controllers;

// No role is presumed: the configured policy is enforced against live Identity data by the service.
[Area("EvaluationCommittee"), Authorize]
public class WeightApprovalsController(TenderWorkflowService workflow, ProtectedDocumentStore documents) : WorkflowControllerBase(workflow, documents)
{
    public override async Task<IActionResult> Index()
    {
        TenderWorkflowService.Require(await Workflow.WeightApprover(await Workflow.Actor(User)), "Awaiting Approval Policy, or you are not a configured weight approver.");
        return await base.Index();
    }
    public override async Task<IActionResult> Details(int id)
    {
        TenderWorkflowService.Require(await Workflow.WeightApprover(await Workflow.Actor(User)), "Awaiting Approval Policy, or you are not a configured weight approver.");
        return await base.Details(id);
    }
    [HttpPost] public Task<IActionResult> Approve(int id) => Execute(id, () => Workflow.ApproveWeights(id, User));
}
