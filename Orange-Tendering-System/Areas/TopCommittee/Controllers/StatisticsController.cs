using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = SystemRoles.TopCommittee)]
public sealed class StatisticsController(ApplicationDbContext db, UserManager<ApplicationUser> users) : TopCommitteeControllerBase(db, users)
{
    public async Task<IActionResult> Index() => await ActiveUser() is null ? Forbid() : View(await Committee.Statistics());
    public async Task<IActionResult> Export()
    {
        if (await ActiveUser() is null) return Forbid();
        var report = await Committee.Statistics();
        return File(StatisticsWorkbook.Create(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TopCommittee-Statistics-{report.GeneratedAt:yyyyMMdd-HHmm}.xlsx");
    }
}
