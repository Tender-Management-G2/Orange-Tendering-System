// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: UNASSIGNED — Team assignment required
// Module: Supplier Portal
// Purpose: MVC endpoints for the Invitations pages of the Supplier workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.Supplier.Controllers
{
    [Area("Supplier")]
    [Authorize(Roles = SystemRoles.Supplier)]
    public class InvitationsController : Controller
    {
        // My Invitations — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Supplier/Invitations. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Invitation Details — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Supplier/Invitations/Details/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Details(int? id)
        {
            return View();
        }

        // Accept / Decline Invitation — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Supplier/Invitations/Respond/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Respond(int? id)
        {
            return View();
        }
    }
}
