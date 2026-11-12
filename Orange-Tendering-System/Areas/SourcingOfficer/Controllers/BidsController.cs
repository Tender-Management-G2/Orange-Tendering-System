// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Saja
// Module: Sourcing Officer / Bid Management
// Purpose: MVC endpoints for the Bids pages of the Sourcing Officer workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers
{
    [Area("SourcingOfficer")]
    [Authorize(Roles = SystemRoles.SourcingOfficer)]
    public class BidsController : Controller
    {
        // Received Bids — Owner: Saja · Trello: Received Bids
        // GET /SourcingOfficer/Bids/Index/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Index(int? id)
        {
            return View();
        }

        // Bid Details & Documents — Owner: Saja · Trello: Bid Details & Documents
        // GET /SourcingOfficer/Bids/Details/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Details(int? id)
        {
            return View();
        }

        // Bid Compliance Check — Owner: Saja · Trello: Bid Compliance Check
        // GET /SourcingOfficer/Bids/Compliance/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Compliance(int? id)
        {
            return View();
        }

        // Bid Comparison — Owner: Saja · Trello: Bid Comparison
        // GET /SourcingOfficer/Bids/Compare/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Compare(int? id)
        {
            return View();
        }

        // Forward Bids to Evaluation Committee — Owner: Saja · Trello: Forward Bids to Evaluation Committee
        // GET /SourcingOfficer/Bids/ForwardToCommittee/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult ForwardToCommittee(int? id)
        {
            return View();
        }

        // Bid & Clarification History — Owner: Saja · Trello: Bid & Clarification History
        // GET /SourcingOfficer/Bids/History/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult History(int? id)
        {
            return View();
        }
    }
}
