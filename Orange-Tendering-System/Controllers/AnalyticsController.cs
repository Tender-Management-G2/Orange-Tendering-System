// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: UNASSIGNED — Team assignment required
// Module: Reports, Analytics & Exports
// Purpose: MVC endpoints for the Analytics pages of the shared signed-in pages.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Controllers
{
    [Authorize(Roles = SystemRoles.Admin + "," + SystemRoles.SourcingOfficer + "," + SystemRoles.SourcingManager + "," + SystemRoles.TopCommittee)]
    public class AnalyticsController : Controller
    {
        // Reports & Analytics — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Analytics. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Purchase Request Reports — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Analytics/PurchaseRequests. TODO: query the data in a service and pass a view model.
        public IActionResult PurchaseRequests()
        {
            return View();
        }

        // Tender Reports — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Analytics/Tenders. TODO: query the data in a service and pass a view model.
        public IActionResult Tenders()
        {
            return View();
        }

        // Supplier Reports — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Analytics/Suppliers. TODO: query the data in a service and pass a view model.
        public IActionResult Suppliers()
        {
            return View();
        }

        // Bids & Evaluation Reports — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Analytics/Evaluations. TODO: query the data in a service and pass a view model.
        public IActionResult Evaluations()
        {
            return View();
        }

        // Approvals & Governance Reports — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Analytics/Governance. TODO: query the data in a service and pass a view model.
        public IActionResult Governance()
        {
            return View();
        }
    }
}
