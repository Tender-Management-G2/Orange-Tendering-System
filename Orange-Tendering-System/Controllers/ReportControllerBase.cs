using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Controllers;

// Shared HTTP transport only. All concrete report controllers and views stay in their role Areas.
[AutoValidateAntiforgeryToken]
public abstract class ReportControllerBase(TenderWorkflowService workflow) : Controller
{
    protected TenderWorkflowService Workflow => workflow;
    public IActionResult Index() => RedirectToAction("Index", "Workflow");
    public async Task<IActionResult> Review(int id) { PrivateResponse(); return View(await workflow.ReviewReport(id, User)); }
    private void PrivateResponse()
    {
        Response.Headers.CacheControl = "private, no-store, max-age=0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'";
    }
    public async Task<IActionResult> Pdf(int id, bool download = false)
    {
        var copy = await workflow.ReportPdf(id, User); PrivateResponse();
        Response.Headers["X-Canonical-SHA256"] = copy.CanonicalHash;
        Response.Headers["X-Access-Copy-SHA256"] = copy.CopyHash;
        return download ? File(copy.Bytes, "application/pdf", copy.Name) : File(copy.Bytes, "application/pdf");
    }
    public async Task<IActionResult> Verify(int id) { PrivateResponse(); return Json(await workflow.VerifyReport(id, User)); }
    public async Task<IActionResult> Receipt(long id, bool manifest = false)
    { var receipt = await workflow.SignatureReceipt(id, User, manifest); PrivateResponse(); return File(receipt.Bytes, receipt.Type, receipt.Name); }
    [HttpPost] public Task<IActionResult> Return(int id, ReturnReportInput input) => Execute(id, () => workflow.RequestReportRevision(id, input, User));
    [HttpPost] public Task<IActionResult> ActivateRevision(int id, int revisionId) => Execute(id, () => workflow.AuthorizeBlockedRevision(revisionId, User));
    [HttpPost] public async Task<IActionResult> Resubmit(int id, int revisionId, string correction)
    {
        ValidateForm(); await workflow.ResubmitReportRevision(revisionId, correction, User);
        var report = await workflow.ReviewReport(id, User); TempData["ReportMessage"] = "A new report version was generated. All required signatures must be collected again.";
        return RedirectToAction(nameof(Review), new { id = report.Versions.First().Id });
    }
    protected async Task<IActionResult> Execute(int id, Func<Task> action)
    { ValidateForm(); await action(); TempData["ReportMessage"] = "The report lifecycle action was saved."; return RedirectToAction(nameof(Review), new { id }); }
    private void ValidateForm()
    { if (!ModelState.IsValid) throw new WorkflowException(string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage))); }
    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is WorkflowException or DbUpdateException)
        {
            ViewData.Model = context.Exception is WorkflowException e ? e.Message : "A concurrent or duplicate operation was rejected. Reopen the current report.";
            context.ExceptionHandled = true; context.Result = new ViewResult { ViewName = "WorkflowError", ViewData = ViewData, TempData = TempData, StatusCode = 400 };
        }
        base.OnActionExecuted(context);
    }
}
