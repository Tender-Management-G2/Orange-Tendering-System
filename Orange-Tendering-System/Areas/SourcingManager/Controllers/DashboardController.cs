// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Haya
// Module: Sourcing Manager Review
// Purpose: MVC endpoints for the Dashboard pages of the Sourcing Manager workspace.
// Status: Dashboard data comes from InternalDashboardService (real, role-scoped SQL Server counts).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingManager.Controllers
{
    [Area("SourcingManager")]
    [Authorize(Roles = SystemRoles.SourcingManager)]
    public class DashboardController : Controller
    {
        // Sourcing Manager Dashboard — Owner: Haya · Trello: Sourcing Manager Dashboard (https://trello.com/c/ZkMN8s14)
        // GET /SourcingManager. TODO: build the view model from real data in a service.
        public async Task<IActionResult> Index([FromServices] Orange_Tendering_System.Services.InternalDashboardService dashboard) => View(await dashboard.GetAsync(SystemRoles.SourcingManager, User));
    }
}
