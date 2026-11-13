using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.SourcingManager.Controllers;

[Area("SourcingManager"), Authorize(Roles = SystemRoles.SourcingManager)]
public class WorkflowController(TenderWorkflowService workflow, ProtectedDocumentStore documents) : WorkflowControllerBase(workflow, documents)
{
    [HttpPost] public Task<IActionResult> DecideProceed(int id, int decisionId, bool approve, string comment) => Execute(id, () => Workflow.DecideProceedException(id, decisionId, approve, comment, User));
    [HttpPost] public Task<IActionResult> Decision(int id, string decision, string comment = "", string? documentHash = null, bool confirmed = false) => Execute(id, () => Workflow.ManagerDecision(id, decision, comment, User, documentHash, confirmed));
}
