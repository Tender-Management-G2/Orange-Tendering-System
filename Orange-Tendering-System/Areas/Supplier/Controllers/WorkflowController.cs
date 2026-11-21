using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Orange_Tendering_System.Areas.Supplier.Controllers;

[Area("Supplier"), Authorize(Roles = SystemRoles.Supplier)]
public class WorkflowController(TenderWorkflowService workflow, ProtectedDocumentStore documents, SupplierTenderPortalService portal) : WorkflowControllerBase(workflow, documents)
{
    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is WorkflowException error &&
            new[] { "weight", "evaluation", "committee", "approval" }.Any(word => error.Message.Contains(word, StringComparison.OrdinalIgnoreCase)))
            context.Exception = new WorkflowException("This action is not available right now. Check the tender dates or contact the Sourcing Officer.");
        base.OnActionExecuted(context);
    }
    public override async Task<IActionResult> Details(int id)
    {
        var model = await portal.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id);
        if (model is null) return NotFound();
        if (!IsAjax) return RedirectToAction("Index", new { open = id });
        Response.Headers.CacheControl = "no-store";
        return PartialView("_SupplierWorkflowPopup", model);
    }
    [HttpGet]
    public async Task<IActionResult> TenderDetails(int id)
    {
        // Reuse the existing server-side access checks and supplier-filtered workflow data.
        var model = await portal.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id);
        if (model is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return PartialView("_TenderDetails", model);
    }

    [HttpPost] public Task<IActionResult> Respond(int id, int invitationId, bool accept) => Execute(id, () => Workflow.Respond(id, invitationId, accept, User));
    [HttpPost] public Task<IActionResult> SubmitBid(int id, BidInput input) => Execute(id, () => Workflow.SubmitBid(id, input, User));
    [HttpPost, RequestSizeLimit(21 * 1024 * 1024)] public Task<IActionResult> Upload(int id, int bidId, IFormFile file, CancellationToken ct) => Execute(id, () => Documents.Upload(id, bidId, file, User, ct));
}
