using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.EvaluationCommittee.Controllers;

[Area("EvaluationCommittee"), Authorize(Roles = SystemRoles.EvaluationCommittee + "," + SystemRoles.SourcingManager)]
public class WorkflowController(TenderWorkflowService workflow, ProtectedDocumentStore documents) : WorkflowControllerBase(workflow, documents)
{
    [HttpPost] public Task<IActionResult> Propose(int id, decimal? financial, decimal? technical) => Execute(id, () =>
    {
        TenderWorkflowService.Require(financial.HasValue && technical.HasValue, "Both weight percentages must be supplied.");
        return Workflow.Propose(id, financial.Value, technical.Value, User);
    });
    [HttpPost] public Task<IActionResult> SubmitEvaluation(int id, List<ScoreInput> scores) => Execute(id, () => Workflow.SubmitEvaluation(id, scores, User));
    [HttpPost] public Task<IActionResult> GenerateReport(int id, string justification) => Execute(id, () => Workflow.GenerateReport(id, justification, User));
    [HttpPost] public Task<IActionResult> FinalizeReport(int id, string comment = "", string? documentHash = null, bool confirmed = false) => Execute(id, () => Workflow.FinalizeReport(id, comment, User, documentHash, confirmed));
}
