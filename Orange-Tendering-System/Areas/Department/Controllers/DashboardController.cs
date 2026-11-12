// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Doulat
// Module: Department / Purchase Requests
// Purpose: MVC endpoints for the Dashboard pages of the Requesting Department workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
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
        // Department Dashboard — Owner: Doulat · Trello: Department Dashboard
        // GET /Department. TODO: build the view model from real data in a service.
        public IActionResult Index()
        {
            return View(new WorkspaceDashboardViewModel());
        }
    }
}
