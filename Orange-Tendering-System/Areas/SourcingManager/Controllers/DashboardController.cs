// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Haya
// Module: Sourcing Manager Review
// Purpose: MVC endpoints for the Dashboard pages of the Sourcing Manager workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
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
        // Sourcing Manager Dashboard — Owner: Haya · Trello: Sourcing Manager Dashboard
        // GET /SourcingManager. TODO: build the view model from real data in a service.
        public IActionResult Index()
        {
            return View(new WorkspaceDashboardViewModel());
        }
    }
}
