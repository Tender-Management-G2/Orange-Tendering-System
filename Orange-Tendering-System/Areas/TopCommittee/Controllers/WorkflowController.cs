using Orange_Tendering_System.Areas.TopCommittee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = TopExecutiveRoles.Allowed)]
public class WorkflowController(IServiceProvider services, ProtectedDocumentStore documents) : WorkflowControllerBase(TopCommitteeWorkflow.Create(services), documents)
{
    [HttpPost] public Task<IActionResult> Vote(int id, VoteDecision decision, string? comment, string? documentHash = null, bool confirmed = false, TopRejectionReason? rejectionReason = null) => Execute(id, () => Workflow.TopVote(id, decision, comment ?? "", User, documentHash, confirmed, rejectionReason: rejectionReason));
    // Pre-tender review of the prepared tender (not an award): Approve, or Return with a reason.
    [HttpPost] public Task<IActionResult> PreTender(int id, PreTenderDecision decision, string? comment) => Execute(id, () => Workflow.PreTenderVote(id, decision, comment, User));
}
