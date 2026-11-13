using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = SystemRoles.TopCommittee)]
public class WorkflowController(TenderWorkflowService workflow, ProtectedDocumentStore documents) : WorkflowControllerBase(workflow, documents)
{
    [HttpPost] public Task<IActionResult> Vote(int id, VoteDecision decision, string comment, string? documentHash = null, bool confirmed = false) => Execute(id, () => Workflow.TopVote(id, decision, comment, User, documentHash, confirmed));
}
