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
        // Tender Passport: an authorized internal visit is audited once per visit window (refreshes are not re-recorded).
        if (!model.IsSupplier) await Service<AuditLogService>().RecordViewAsync(model.UserId, "Tender", id.ToString(), new { Round = model.Tender.Rounds.Select(r => (int?)r.RoundNumber).Max() });
        Response.Headers.CacheControl = "no-store";
        return PartialView("~/Views/Shared/_WorkflowPopup.cshtml", model);
    }
    // Full Activity Trail of the Tender Passport. Uses the same authorization as Details; never available to suppliers.
    [HttpGet]
    public async Task<IActionResult> Passport(int id)
    {
        var model = await workflow.Details(id, User);
        if (model.IsSupplier) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return PartialView("~/Views/Shared/_TenderPassportTrail.cshtml", await Service<TenderPassportService>().TrailAsync(model));
    }
    // The reviewed AI analysis attached to the evaluation meeting, as a PDF for the Officer and the tender's committees.
    [HttpGet]
    public async Task<IActionResult> AiAnalysisPdf(int id)
    {
        var model = await workflow.Details(id, User);
        var session = model.Session;
        var ai = session == null ? null : model.Analyses.FirstOrDefault(x => x.Id == session.AiAnalysisId);
        if (session == null || ai == null) return NotFound();
        var pdf = Services.Reports.AiAnalysisPdf.Build(ai, model.Tender.Title, model.Tender.Id, session);
        var marks = Service<DocumentWatermarkService>(); var audit = Service<AuditLogService>();
        var copy = marks.Apply(pdf, new WatermarkStamp(await marks.IdentityAsync(model.UserId), audit.UtcNow, TenderPassportService.Reference(model.Tender.Id), $"AI analysis #{ai.Id}", "Downloaded"));
        await audit.RecordAsync(model.UserId, AuditLogService.AiAnalysisPdfDownloaded, "Tender", id.ToString(), new { AiAnalysisId = ai.Id, Scope = "Bid", Watermarked = copy.Applied });
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(copy.Bytes, "application/pdf", $"AI-analysis-T-{model.Tender.Id:0000}.pdf");
    }
    public async Task<IActionResult> Download(int id)
    {
        var (document, tender) = await workflow.AuthorizedDocument(id, User);
        var path = documents.Resolve(document);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        var userId = (await workflow.Actor(User)).Id;
        var scope = document.BidId != null ? "Bid" : document.TenderReportId != null ? "Report" : "Tender";
        var audit = Service<AuditLogService>();
        // PDFs are served as a personalized, watermarked copy; the stored file is never modified. Other types are served as stored.
        if (string.Equals(Path.GetExtension(document.FileName), ".pdf", StringComparison.OrdinalIgnoreCase) || document.ContentType == "application/pdf")
        {
            var marks = Service<DocumentWatermarkService>();
            var copy = marks.Apply(await System.IO.File.ReadAllBytesAsync(path), new WatermarkStamp(await marks.IdentityAsync(userId), audit.UtcNow,
                TenderPassportService.Reference(tender.Id), "Document #" + document.Id, "Downloaded"));
            await audit.RecordAsync(userId, AuditLogService.DocumentDownloaded, "Tender", tender.Id.ToString(), new { DocumentId = document.Id, document.FileName, Scope = scope, Watermarked = copy.Applied });
            return File(copy.Bytes, copy.Applied ? "application/pdf" : "application/octet-stream", document.FileName);
        }
        await audit.RecordAsync(userId, AuditLogService.DocumentDownloaded, "Tender", tender.Id.ToString(), new { DocumentId = document.Id, document.FileName, Scope = scope, Watermarked = false });
        return PhysicalFile(path, "application/octet-stream", document.FileName);
    }
    // Resolved per request so the seven area controllers keep their constructors (the Top Committee one builds its own workflow service).
    private T Service<T>() where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();
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
