// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Top Committee - Management Statistics: site-wide statistics and transactions per supplier, downloadable as Excel / PDF.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

<<<<<<< HEAD
namespace Orange_Tendering_System.Areas.TopCommittee.Controllers
=======
namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = TopExecutiveRoles.Allowed)]
public sealed class StatisticsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IWebHostEnvironment environment, IRazorViewEngine views) : TopCommitteeControllerBase(db, users)
>>>>>>> be32000 (temp)
{
    [Area("TopCommittee")]
    [Authorize(Roles = SystemRoles.TopCommittee)]
    public class StatisticsController(ReportingService reports) : Controller
    {
        public async Task<IActionResult> Index() => View(await reports.BuildAsync("management"));
    }
}
