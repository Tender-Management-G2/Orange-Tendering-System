using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.SourcingManager.Controllers;

// Legacy Sourcing Manager clarification URLs. Every role uses the one Role-Based Clarification Chat
// (/Clarifications, ClarificationService + ClarificationPolicy), so a manager can only talk to the
// Evaluation Committee members and Top Committee members of tenders the manager is assigned to, and
// replies/resolutions are re-checked against the approved role pairs on every request.
[Area("SourcingManager")]
[Authorize(Roles = SystemRoles.SourcingManager + "," + SystemRoles.EvaluationCommittee + "," + SystemRoles.TopCommittee)]
public sealed class ClarificationsController(ClarificationService clarifications) : Controller
{
    [HttpGet]
    public IActionResult Index(string? search, string? status) =>
        RedirectToAction("Index", "Clarifications", new { area = "", search, status });

    [HttpGet]
    public IActionResult Details(int id) => RedirectToAction("Details", "Clarifications", new { area = "", id });

    [Authorize(Roles = SystemRoles.SourcingManager)]
    [HttpGet]
    public IActionResult Create(int? tenderId) => RedirectToAction("New", "Clarifications", new { area = "" });

    // Old form posts are checked by the shared policy (manager must be assigned to the tender; the recipient must be
    // an Evaluation or Top Committee member of that tender).
    [Authorize(Roles = SystemRoles.SourcingManager)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int tenderId, string toUserId, string subject, string message)
    {
        try
        {
            var id = await clarifications.CreateAsync(User, toUserId, "Tender", tenderId, subject, message);
            TempData.Orange(OrangeFeedback.Toast("Clarification sent!", "The recipient was notified.", OrangeVariant.Send));
            return RedirectToAction("Details", "Clarifications", new { area = "", id });
        }
        catch (WorkflowException e)
        {
            TempData["ClarificationError"] = e.Message;
            return RedirectToAction("New", "Clarifications", new { area = "" });
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string message)
    {
        if (!await clarifications.ReplyAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, message)) return NotFound();
        TempData.Orange(OrangeFeedback.Toast("Reply sent", "The other participant was notified.", OrangeVariant.Send));
        return RedirectToAction("Details", "Clarifications", new { area = "", id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id)
    {
        if (!await clarifications.ResolveAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id)) return NotFound();
        TempData.Orange(OrangeFeedback.Toast("Clarification resolved", "The conversation is marked as resolved.", OrangeVariant.Approve));
        return RedirectToAction("Details", "Clarifications", new { area = "", id });
    }
}
