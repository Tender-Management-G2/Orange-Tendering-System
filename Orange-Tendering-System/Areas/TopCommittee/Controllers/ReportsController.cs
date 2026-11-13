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
public sealed class ReportsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IWebHostEnvironment environment, IRazorViewEngine views) : TopCommitteeControllerBase(db, users)
{
    public async Task<IActionResult> Index()
    {
        if (await ActiveUser() is null) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return View("Index", new ReportingPage(await new TopCommitteeReportingService(Db).Read(), false));
    }

    [HttpGet("/Analytics", Order = -1), TopCommitteeReportsRoute]
    public Task<IActionResult> ExistingSidebarEntry() => Index();

    public async Task<IActionResult> ExportExcel()
    {
        if (await ActiveUser() is null) return Forbid();
        var page = new ReportingPage(await new TopCommitteeReportingService(Db).Read(), false);
        Response.Headers.CacheControl = "no-store";
        return File(ReportingWorkbook.Create(page), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TopCommittee-Reports-{page.Data.GeneratedAt:yyyyMMdd-HHmm}.xlsx");
    }

    public async Task<IActionResult> ExportPdf(CancellationToken cancellationToken)
    {
        if (await ActiveUser() is null) return Forbid();
        var page = new ReportingPage(await new TopCommitteeReportingService(Db).Read(), false);
        Response.Headers.CacheControl = "no-store";
        try { return File(await ReportingExport.Pdf(this, views, page, environment.ContentRootPath, cancellationToken), "application/pdf", $"TopCommittee-Reports-{page.Data.GeneratedAt:yyyyMMdd-HHmm}.pdf"); }
        catch (InvalidOperationException exception) { return Problem(title: "PDF export unavailable", detail: exception.Message, statusCode: 503); }
    }
}

