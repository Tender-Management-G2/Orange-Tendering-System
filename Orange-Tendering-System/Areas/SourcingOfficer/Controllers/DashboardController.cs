// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Amal
// Module: Sourcing Officer / Tender Management
// Purpose: MVC endpoints for the Dashboard pages of the Sourcing Officer workspace.
// Status: Dashboard data comes from InternalDashboardService (real, role-scoped SQL Server counts).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers
{
    [Area("SourcingOfficer")]
    [Authorize(Roles = SystemRoles.SourcingOfficer)]
    public class DashboardController : Controller
    {
        // Sourcing Officer Dashboard — Owner: Amal · Trello: Sourcing Officer Dashboard (https://trello.com/c/VeRtD4Tn)
        // GET /SourcingOfficer. TODO: build the view model from real data in a service.
        public async Task<IActionResult> Index([FromServices] Orange_Tendering_System.Services.InternalDashboardService dashboard) => View(await dashboard.GetAsync(SystemRoles.SourcingOfficer, User));
    }
}
