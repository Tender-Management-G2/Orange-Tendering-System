using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer"), Authorize(Roles = SystemRoles.SourcingOfficer)]
public class WorkflowController(TenderWorkflowService workflow, ProtectedDocumentStore documents, OllamaBidAnalysisService ai, EvaluationSchedulingService scheduling)
    : WorkflowControllerBase(workflow, documents)
{
    [HttpGet] public IActionResult Create(int? id) => View(new RequirementsInput { PurchaseRequestId = id ?? 0 });
    [HttpPost] public async Task<IActionResult> Create(RequirementsInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var id = await Workflow.Create(input, User);
        return RedirectToAction(nameof(Details), new { id });
    }
    [HttpPost] public Task<IActionResult> RequestWeights(int id) => Execute(id, () => Workflow.RequestWeights(id, User));
    [HttpPost] public Task<IActionResult> OpenBidding(int id, DateTimeOffset deadline) => Execute(id, () => Workflow.OpenBidding(id, deadline, User));
    [HttpPost] public Task<IActionResult> Invite(int id, int supplierId) => Execute(id, () => Workflow.Invite(id, supplierId, User));
    [HttpPost] public Task<IActionResult> CloseBidding(int id) => Execute(id, () => Workflow.CloseBidding(id, User));
    [HttpPost] public Task<IActionResult> Retender(int id, string reason) => Execute(id, () => Workflow.Retender(id, reason, User));
    [HttpPost] public Task<IActionResult> Proceed(int id) => Execute(id, () => Workflow.Proceed(id, User));
    [HttpPost] public Task<IActionResult> RequestProceed(int id, string justification) => Execute(id, () => Workflow.RequestProceedException(id, justification, User));
    [HttpPost] public Task<IActionResult> InviteExternal(int id, string companyName, string email, string? phone = null) => Execute(id, () => Workflow.AddExternalSupplier(id, companyName, email, phone, User));
    [HttpPost] public Task<IActionResult> ReviseReport(int id, string correction) => Execute(id, () => Workflow.ReviseReport(id, correction, User));
    [HttpPost] public Task<IActionResult> Compliance(int id, int bidId, ComplianceStatus status, string notes, string missing = "") => Execute(id, () => Workflow.Compliance(id, bidId, status, notes, missing, User));
    [HttpPost] public Task<IActionResult> Setup(int id, SessionInput input, CancellationToken ct) => Execute(id, () => scheduling.Schedule(id, input, User, ct));
    [HttpPost] public Task<IActionResult> EndEvaluation(int id, bool confirmed, string reason) => Execute(id, () => Workflow.EndEvaluation(id, confirmed, reason, User));
    [HttpPost] public Task<IActionResult> Analyze(int id, CancellationToken ct) => Execute(id, () => ai.Analyze(id, User, ct));
    [HttpPost] public Task<IActionResult> ReviewAi(int id, int analysisId) => Execute(id, () => Workflow.ReviewAi(id, analysisId, User));
    [HttpPost] public Task<IActionResult> Forward(int id) => Execute(id, () => Workflow.Forward(id, User));
    [HttpPost] public Task<IActionResult> AssignTop(int id, string[] memberIds) => Execute(id, () => Workflow.AssignTop(id, memberIds, User));
    [HttpPost] public Task<IActionResult> Award(int id) => Execute(id, () => Workflow.Award(id, User));
    [HttpPost, RequestSizeLimit(21 * 1024 * 1024)] public Task<IActionResult> Upload(int id, IFormFile file, CancellationToken ct) => Execute(id, () => Documents.Upload(id, null, file, User, ct));
}
