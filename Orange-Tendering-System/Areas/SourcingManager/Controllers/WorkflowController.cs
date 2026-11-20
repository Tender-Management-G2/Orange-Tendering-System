using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingManager.Controllers;

[Area("SourcingManager"), Authorize(Roles = SystemRoles.SourcingManager)]
public class WorkflowController(TenderWorkflowService workflow, ProtectedDocumentStore documents) : WorkflowControllerBase(workflow, documents)
{
    [HttpPost] public Task<IActionResult> DecideProceed(int id, int decisionId, bool approve, string comment) => Execute(id, () => Workflow.DecideProceedException(id, decisionId, approve, comment, User));
    [HttpPost] public Task<IActionResult> Decision(int id, string decision, string comment = "", string? documentHash = null, bool confirmed = false) => Execute(id, () => Workflow.ManagerDecision(id, decision, comment, User, documentHash, confirmed));

    // Executive summary for the Top Committee: pre-filled from recorded facts, edited by the Manager, then submitted once.
    [HttpGet]
    public async Task<IActionResult> ExecutiveSummary(int id)
    {
        try { Response.Headers.CacheControl = "no-store"; return View(await Workflow.ExecutiveSummary(id, User)); }
        catch (WorkflowException e) { ViewData.Model = e.Message; return View("WorkflowError"); }
    }

    [HttpPost]
    public async Task<IActionResult> ExecutiveSummary(int id, [Bind(Prefix = "Input")] ExecutiveSummaryInput input, string intent, string? documentHash, bool confirmed, string? signatureImage)
    {
        try
        {
            if (!ModelState.IsValid) throw new WorkflowException(string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage).Distinct()));
            if (intent == "submit")
            {
                await Workflow.SubmitExecutiveSummary(id, input, User, documentHash ?? "", confirmed, string.IsNullOrWhiteSpace(signatureImage) ? null : ReportControllerBase.ReadSignature(signatureImage));
                TempData.Orange(OrangeFeedback.Popup("Executive summary submitted!", "The Top Committee was notified. Four approvals award the tender automatically.", OrangeVariant.Send));
                return RedirectToAction(nameof(Index), new { open = id });
            }
            await Workflow.SaveExecutiveSummary(id, input, User);
            TempData.Orange(OrangeFeedback.Toast("Draft saved", "Your executive summary draft is saved. You can keep editing it until you submit."));
            return RedirectToAction(nameof(ExecutiveSummary), new { id });
        }
        catch (WorkflowException e)
        {
            var page = await Workflow.ExecutiveSummary(id, User);
            page.Input = input; ViewData["SummaryError"] = e.Message;
            return View(page);
        }
    }
}
