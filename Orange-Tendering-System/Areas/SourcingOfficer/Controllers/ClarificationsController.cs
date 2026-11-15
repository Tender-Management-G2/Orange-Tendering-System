using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using System.Security.Claims;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

// Keep the legacy area URLs; all participants now use the same authorized inbox and reply service.
[Area("SourcingOfficer"), Authorize(Roles = SystemRoles.SourcingOfficer)]
public class ClarificationsController(ClarificationService clarifications) : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Clarifications", new { area = "" });
    public IActionResult Details(int id) => RedirectToAction("Details", "Clarifications", new { area = "", id });
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string message)
    {
        if (!ModelState.IsValid || !await clarifications.ReplyAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, message)) return NotFound();
        return RedirectToAction("Details", "Clarifications", new { area = "", id });
    }
}
