// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: UNASSIGNED — Team assignment required
// Module: Supplier Portal
// Purpose: MVC endpoints for the Profile pages of the Supplier workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.Supplier.Controllers
{
    [Area("Supplier")]
    [Authorize(Roles = SystemRoles.Supplier)]
    public class ProfileController : Controller
    {
        // Company Profile — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Supplier/Profile. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }
    }
}
