// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Doulat
// Module: Department / Purchase Requests
// Purpose: MVC endpoints for the Dashboard pages of the Requesting Department workspace.
// Status: Dashboard data comes from InternalDashboardService (real, role-scoped SQL Server counts).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Department.Controllers
{
    [Area("Department")]
    [Authorize(Roles = SystemRoles.RequestingDepartment)]
    public class DashboardController : Controller
    {
        // Department Dashboard — Owner: Doulat · Trello: Department Dashboard (https://trello.com/c/033sNEkN)
        // GET /Department. TODO: build the view model from real data in a service.
        public async Task<IActionResult> Index([FromServices] Orange_Tendering_System.Services.InternalDashboardService dashboard) => View(await dashboard.GetAsync(SystemRoles.RequestingDepartment, User));
    }
}
