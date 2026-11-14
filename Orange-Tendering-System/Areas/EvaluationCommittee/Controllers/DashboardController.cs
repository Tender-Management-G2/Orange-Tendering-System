// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Tariq
// Module: Technical & Financial Evaluation Committee
// Purpose: MVC endpoints for the Dashboard pages of the Evaluation Committee workspace.
// Status: Dashboard data comes from InternalDashboardService (real, role-scoped SQL Server counts).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.EvaluationCommittee.Controllers
{
    [Area("EvaluationCommittee")]
    [Authorize(Roles = SystemRoles.EvaluationCommittee)]
    public class DashboardController : Controller
    {
        // Committee Dashboard — Owner: Tariq · Trello: Committee Dashboard (https://trello.com/c/Z1VGmfZ7)
        // GET /EvaluationCommittee. TODO: build the view model from real data in a service.
        public async Task<IActionResult> Index([FromServices] Orange_Tendering_System.Services.InternalDashboardService dashboard) => View(await dashboard.GetAsync(SystemRoles.EvaluationCommittee, User));
    }
}
