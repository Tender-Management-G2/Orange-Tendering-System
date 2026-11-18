using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers;

// Only genuinely shared HTTP behavior lives here. Role actions and views live in Areas.
[AutoValidateAntiforgeryToken]
public abstract class WorkflowControllerBase(TenderWorkflowService workflow, ProtectedDocumentStore documents) : Controller
{
    protected TenderWorkflowService Workflow => workflow;
    protected ProtectedDocumentStore Documents => documents;
    public virtual async Task<IActionResult> Index() => View(await workflow.List(User));
    // The tender workflow is a popup over the list (Views/Shared/_WorkflowPopup.cshtml). The popup loads this action by AJAX;
    // opening the address directly shows the list with the popup already open for this tender.
    protected bool IsAjax => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    /// <summary>Set inside an Execute action when the result needs facts only the action knows (for example how many were invited).</summary>
    protected OrangeFeedback? ActionFeedback { get; set; }
    public virtual async Task<IActionResult> Details(int id)
    {
        var model = await workflow.Details(id, User);
        if (!IsAjax) return RedirectToAction("Index", new { open = id });
        Response.Headers.CacheControl = "no-store";
        return PartialView("~/Views/Shared/_WorkflowPopup.cshtml", model);
    }
    public async Task<IActionResult> Download(int id)
    {
        var (document, _) = await workflow.AuthorizedDocument(id, User);
        var path = documents.Resolve(document);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(path, "application/octet-stream", document.FileName);
    }
    protected async Task<IActionResult> Execute(int id, Func<Task> action)
    {
        try
        {
        if (!ModelState.IsValid) throw new WorkflowException(string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => string.IsNullOrEmpty(x.ErrorMessage) ? "Invalid form value." : x.ErrorMessage)));
        await action();
        // Orange Motion feedback for what was actually committed (Services/OrangeWorkflowFeedback.cs).
        var feedback = ActionFeedback ?? await OrangeWorkflowFeedback.For(this, workflow, id);
        if (IsAjax) return Json(new { ok = true, feedback = feedback?.ToJsonElement() });
        if (feedback is not null) TempData.Orange(feedback);
        else TempData["WorkflowMessage"] = "The operation was saved successfully.";
        return RedirectToAction(nameof(Details), new { id });
        }
        catch (WorkflowException error)
        {
            // Expected business-rule refusals are handled here, before they escape
            // into MVC's action filter (and appear user-unhandled in the debugger).
            return WorkflowFailure(error.Message);
        }
        catch (DbUpdateException)
        {
            return WorkflowFailure("The record changed or a duplicate operation was rejected. Reload and check its current state.");
        }
    }
    private IActionResult WorkflowFailure(string message)
    {
        if (IsAjax) return BadRequest(new { error = message });
        ViewData.Model = message;
        return new ViewResult { ViewName = "WorkflowError", ViewData = ViewData, TempData = TempData, StatusCode = 400 };
    }
    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is WorkflowException or DbUpdateException)
        {
            var message = context.Exception is WorkflowException error ? error.Message : "The record changed or a duplicate operation was rejected. Reload and check its current state.";
            context.ExceptionHandled = true;
            if (IsAjax) { context.Result = new JsonResult(new { error = message }) { StatusCode = 400 }; base.OnActionExecuted(context); return; }
            ViewData.Model = message;
            context.Result = new ViewResult { ViewName = "WorkflowError", ViewData = ViewData, TempData = TempData, StatusCode = 400 };
        }
        base.OnActionExecuted(context);
    }
}
