using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Supplier.Controllers;

[Area("Supplier"), Authorize(Roles = SystemRoles.Supplier)]
public class WorkflowController(TenderWorkflowService workflow, ProtectedDocumentStore documents) : WorkflowControllerBase(workflow, documents)
{
    [HttpPost] public Task<IActionResult> Respond(int id, int invitationId, bool accept) => Execute(id, () => Workflow.Respond(id, invitationId, accept, User));
    [HttpPost] public Task<IActionResult> SubmitBid(int id, BidInput input) => Execute(id, () => Workflow.SubmitBid(id, input, User));
    [HttpPost, RequestSizeLimit(21 * 1024 * 1024)] public Task<IActionResult> Upload(int id, int bidId, IFormFile file, CancellationToken ct) => Execute(id, () => Documents.Upload(id, bidId, file, User, ct));
}
