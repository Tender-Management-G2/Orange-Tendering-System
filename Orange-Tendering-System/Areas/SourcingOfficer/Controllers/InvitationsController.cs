// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Amal
// Module: Sourcing Officer / Tender Management
// Purpose: MVC endpoints for the Invitations pages of the Sourcing Officer workspace.
// Compatibility GET routes: use the persisted, authorized weighted workflow.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers
{
    [Area("SourcingOfficer")]
    [Authorize(Roles = SystemRoles.SourcingOfficer)]
    public class InvitationsController : Controller
    {
        // Supplier Invitations — Owner: Amal · Trello: Supplier Invitations (https://trello.com/c/Qh7GKR8x)
        // GET /SourcingOfficer/Invitations/Index/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Index(int? id)
        {
            return RedirectToAction("Index", "Workflow");
        }
    }
}
