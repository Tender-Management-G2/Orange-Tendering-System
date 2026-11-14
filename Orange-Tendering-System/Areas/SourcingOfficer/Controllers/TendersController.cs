using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

// Compatibility addresses only. All tender creation and mutations use WorkflowController and its service.
[Area("SourcingOfficer"), Authorize(Roles = SystemRoles.SourcingOfficer)]
public class TendersController(ApplicationDbContext db) : Controller
{
    [HttpGet] public IActionResult Index() => RedirectToAction("Index", "Workflow");
    [HttpGet] public IActionResult Create(int id) => RedirectToAction("Index", "Workflow");
    [HttpGet] public IActionResult Details(int id) => RedirectToAction("Details", "Workflow", new { id });
    [HttpGet] public IActionResult Rounds(int id) => RedirectToAction("Details", "Workflow", new { id });
    [HttpGet] public IActionResult Edit(int id) => RedirectToAction("Details", "Workflow", new { id });
    [HttpGet] public IActionResult Timeline(int id) => RedirectToAction("Details", "Workflow", new { id });
    [HttpGet] public IActionResult Committees(int id) => RedirectToAction("Details", "Workflow", new { id });
    [HttpGet]
    public async Task<IActionResult> SelectSuppliers(int id, string? category = null)
    {
        var tenderId = await db.TenderRounds.Where(r => r.Id == id).Select(r => (int?)r.TenderId).SingleOrDefaultAsync();
        return tenderId.HasValue ? RedirectToAction("Details", "Workflow", new { id = tenderId.Value }) : NotFound();
    }
    // Old POST forms cannot bypass committee assignment, approved weights, deadlines or duplicate protection.
    [HttpPost, ValidateAntiForgeryToken] public IActionResult Create(CreateOfficerTenderModel model) => UseTenderPage();
    [HttpPost, ValidateAntiForgeryToken] public IActionResult StartNextRound(int tenderId) => UseTenderPage();
    [HttpPost, ValidateAntiForgeryToken] public IActionResult InviteSupplier(int roundId, int supplierId, string category) => UseTenderPage();
    private IActionResult UseTenderPage() => StatusCode(StatusCodes.Status409Conflict,
        "This form has moved. Open the Tender page to continue the authorized workflow.");
}
