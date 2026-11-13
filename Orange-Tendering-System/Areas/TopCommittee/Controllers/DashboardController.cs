// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Doulat
// Module: Top Committee
// Purpose: MVC endpoints for the Dashboard pages of the Top Committee workspace.
// Status: Dashboard data comes from InternalDashboardService (real, role-scoped SQL Server counts).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers
{
    [Area("TopCommittee")]
    [Authorize(Roles = SystemRoles.TopCommittee)]
    public class DashboardController : Controller
    {
        // Top Committee Dashboard — Owner: Doulat · Trello: Top Committee Dashboard (https://trello.com/c/8Z6bnMsb)
        // GET /TopCommittee. TODO: build the view model from real data in a service.
        public async Task<IActionResult> Index([FromServices] Orange_Tendering_System.Services.InternalDashboardService dashboard) => View(await dashboard.GetAsync(SystemRoles.TopCommittee, User));
    }
}
