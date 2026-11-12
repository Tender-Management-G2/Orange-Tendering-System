// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: UNASSIGNED — Team assignment required
// Module: Supplier Portal
// Purpose: MVC endpoints for the Dashboard pages of the Supplier workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Supplier.Controllers
{
    [Area("Supplier")]
    [Authorize(Roles = SystemRoles.Supplier)]
    public class DashboardController : Controller
    {
        // Supplier Dashboard — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Supplier. TODO: build the view model from real data in a service.
        public IActionResult Index()
        {
            return View(new WorkspaceDashboardViewModel());
        }
    }
}
