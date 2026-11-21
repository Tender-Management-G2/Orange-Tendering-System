using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
public class ReportsController(ApplicationDbContext db, TenderWorkflowService workflow) : ReportControllerBase(workflow)
{
    public override async Task<IActionResult> Index()
    {
        var reports = await db.TenderReports.AsNoTracking().Include(r => r.TenderRound).ThenInclude(r => r.Tender)
            .Where(r => r.ReportType == ReportType.FullEvaluation || r.ReportType == ReportType.OfficerComparison)
            .OrderByDescending(r => r.CreatedAt).ToListAsync();
        var ids = reports.Select(r => r.Id.ToString()).ToList();
        var forwardedIds = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "ForwardEvaluationReportToManager" && ids.Contains(a.EntityId))
            .Select(a => a.EntityId).ToListAsync();
        return View(new OfficerReportsViewModel
        {
            Reports = reports,
            ForwardedReportIds = forwardedIds.Select(int.Parse).ToHashSet()
        });
    }

    public async Task<IActionResult> Details(int id)
    {
        await Workflow.ReviewReport(id, User);
        return RedirectToAction(nameof(Review), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> SubmitToManager(int id) => Forward(id);

    public IActionResult Comparison(int? id) => RedirectToAction("Index", "Workflow");
    public IActionResult EvaluationResult(int? id) => RedirectToAction("Index", "Workflow");
    public IActionResult Returned(int? id) => RedirectToAction("Index", "Workflow");
    public IActionResult History(int? id) => RedirectToAction("Index", "Workflow");

    [HttpPost]
    public Task<IActionResult> Forward(int id) => Execute(id, async () =>
    {
        var report = await Workflow.ReviewReport(id, User);
        TenderWorkflowService.Require(report.IsCurrent, "Only the current finalized report can be forwarded.");
        await Workflow.Forward(report.Tender.Id, User);
    });
}
