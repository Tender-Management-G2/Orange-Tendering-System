// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: UNASSIGNED — Team assignment required
// Module: Sourcing Officer / Reports
// Purpose: MVC endpoints for the Reports pages of the Sourcing Officer workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers
{
    [Area("SourcingOfficer")]
    [Authorize(Roles = SystemRoles.SourcingOfficer)]
    public class ReportsController : Controller
    {
        // Officer Reports — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /SourcingOfficer/Reports. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }
    }
}
