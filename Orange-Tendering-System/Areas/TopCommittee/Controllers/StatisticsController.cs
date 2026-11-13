using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = SystemRoles.TopCommittee)]
public sealed class StatisticsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IWebHostEnvironment environment, IRazorViewEngine views) : TopCommitteeControllerBase(db, users)
{
    public async Task<IActionResult> Index()
    {
        if (await ActiveUser() is null) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return View("System", new ReportingPage(await new TopCommitteeReportingService(Db).Read(), true));
    }
    public Task<IActionResult> FullSystem() => Index();
    public async Task<IActionResult> Management() => await ActiveUser() is null ? Forbid() : View("Index", await Committee.Statistics());
    public async Task<IActionResult> Export()
    {
        if (await ActiveUser() is null) return Forbid();
        var report = await Committee.Statistics();
        return File(StatisticsWorkbook.Create(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TopCommittee-Statistics-{report.GeneratedAt:yyyyMMdd-HHmm}.xlsx");
    }
    public async Task<IActionResult> ExportExcel()
    {
        if (await ActiveUser() is null) return Forbid();
        var page = new ReportingPage(await new TopCommitteeReportingService(Db).Read(), true);
        Response.Headers.CacheControl = "no-store";
        return File(ReportingWorkbook.Create(page), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TopCommittee-FullSystem-{page.Data.GeneratedAt:yyyyMMdd-HHmm}.xlsx");
    }
    public async Task<IActionResult> ExportPdf(CancellationToken cancellationToken)
    {
        if (await ActiveUser() is null) return Forbid();
        var page = new ReportingPage(await new TopCommitteeReportingService(Db).Read(), true);
        Response.Headers.CacheControl = "no-store";
        try { return File(await ReportingExport.Pdf(this, views, page, environment.ContentRootPath, cancellationToken), "application/pdf", $"TopCommittee-FullSystem-{page.Data.GeneratedAt:yyyyMMdd-HHmm}.pdf"); }
        catch (InvalidOperationException exception) { return Problem(title: "PDF export unavailable", detail: exception.Message, statusCode: 503); }
    }
}
