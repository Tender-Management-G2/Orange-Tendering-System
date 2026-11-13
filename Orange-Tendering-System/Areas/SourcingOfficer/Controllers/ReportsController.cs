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
        var report = await db.TenderReports.AsNoTracking().Include(r => r.TenderRound).ThenInclude(r => r.Tender).SingleOrDefaultAsync(r => r.Id == id);
        return report == null ? NotFound() : View(report);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitToManager(int id)
    {
        var committeeReport = await db.TenderReports
            .Include(r => r.TenderRound).ThenInclude(r => r.Tender)
            .SingleOrDefaultAsync(r => r.Id == id && r.ReportType == ReportType.FullEvaluation && r.Status == ReportStatus.Submitted);
        if (committeeReport == null) return NotFound();
        if (await db.AuditLogs.AnyAsync(a => a.Action == "ForwardEvaluationReportToManager"
            && a.EntityName == nameof(TenderReport) && a.EntityId == id.ToString()))
        {
            TempData["ManagerForwarded"] = "This committee report has already been sent to the Sourcing Manager.";
            return RedirectToAction(nameof(Index));
        }

        var managerMember = await db.CommitteeMembers.SingleOrDefaultAsync(m =>
            m.TenderId == committeeReport.TenderRound.TenderId
            && m.CommitteeType == CommitteeType.Evaluation
            && m.Responsibility == CommitteeResponsibility.SourcingManager);
        if (managerMember == null)
        {
            TempData["ManagerForwardError"] = "No Sourcing Manager is assigned to this tender yet. Assign the manager, then try again.";
            return RedirectToAction(nameof(Index));
        }

        committeeReport.TenderRound.Tender.Status = TenderStatus.ManagerReview;
        var now = DateTime.UtcNow;
        db.Notifications.Add(new Notification
        {
            UserId = managerMember.UserId,
            Title = "Evaluation report ready for review",
            Message = $"The Sourcing Officer sent the committee evaluation report for {committeeReport.TenderRound.Tender.Title}.",
            Link = "/SourcingManager/Reports",
            CreatedAt = now
        });
        db.AuditLogs.Add(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value,
            Action = "ForwardEvaluationReportToManager",
            EntityName = nameof(TenderReport),
            EntityId = committeeReport.Id.ToString(),
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        TempData["ManagerForwarded"] = "Committee report sent to the assigned Sourcing Manager.";
        return RedirectToAction(nameof(Index));
    }

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
