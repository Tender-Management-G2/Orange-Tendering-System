using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = TopExecutiveRoles.Allowed)]
public sealed class StatisticsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IWebHostEnvironment environment, IRazorViewEngine views, ReportAccessService reportAccess) : TopCommitteeControllerBase(db, users)
{
    public async Task<IActionResult> Index()
    {
        if (!await CanReadStatistics(HttpContext.RequestAborted)) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return await FullSystemPage();
    }
    public async Task<IActionResult> FullSystem()
    {
        if (!await CanReadStatistics(HttpContext.RequestAborted)) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return await FullSystemPage();
    }
    public async Task<IActionResult> Management()
    {
        if (!await CanReadStatistics(HttpContext.RequestAborted)) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return await FullSystemPage();
    }
    private async Task<IActionResult> FullSystemPage() =>
        View("System", new ReportingPage(await new TopCommitteeReportingService(Db).Read(), true));
    public async Task<IActionResult> Export()
    {
        if (!await CanReadStatistics(HttpContext.RequestAborted)) return Forbid();
        var report = await Committee.Statistics();
        return File(StatisticsWorkbook.Create(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TopCommittee-Statistics-{report.GeneratedAt:yyyyMMdd-HHmm}.xlsx");
    }
    public async Task<IActionResult> ExportExcel()
    {
        if (!await CanReadStatistics(HttpContext.RequestAborted)) return Forbid();
        var page = new ReportingPage(await new TopCommitteeReportingService(Db).Read(), true);
        Response.Headers.CacheControl = "no-store";
        return File(ReportingWorkbook.Create(page), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TopCommittee-FullSystem-{page.Data.GeneratedAt:yyyyMMdd-HHmm}.xlsx");
    }
    public async Task<IActionResult> ExportPdf(CancellationToken cancellationToken)
    {
        if (!await CanReadStatistics(cancellationToken)) return Forbid();
        var page = new ReportingPage(await new TopCommitteeReportingService(Db).Read(), true);
        Response.Headers.CacheControl = "no-store";
        try { return File(await ReportingExport.Pdf(this, views, page, environment.ContentRootPath, cancellationToken), "application/pdf", $"TopCommittee-FullSystem-{page.Data.GeneratedAt:yyyyMMdd-HHmm}.pdf"); }
        catch (InvalidOperationException exception) { return Problem(title: "PDF export unavailable", detail: exception.Message, statusCode: 503); }
    }

    private async Task<bool> CanReadStatistics(CancellationToken ct) =>
        await ActiveUser() is not null && await reportAccess.CanReadAsync("management", User, ct);
}
