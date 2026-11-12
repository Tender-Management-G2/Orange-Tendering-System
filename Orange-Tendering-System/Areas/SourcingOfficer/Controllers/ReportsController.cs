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

        // Officer Comparison Report — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /SourcingOfficer/Reports/Comparison/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Comparison(int? id)
        {
            return View();
        }

        // Report Details — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /SourcingOfficer/Reports/Details/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Details(int? id)
        {
            return View();
        }

        // Committee Result Received — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /SourcingOfficer/Reports/EvaluationResult/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult EvaluationResult(int? id)
        {
            return View();
        }

        // Submit Full Report to Sourcing Manager — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /SourcingOfficer/Reports/SubmitToManager/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult SubmitToManager(int? id)
        {
            return View();
        }

        // Returned Report Review — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /SourcingOfficer/Reports/Returned/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Returned(int? id)
        {
            return View();
        }

        // Report Versions — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /SourcingOfficer/Reports/History/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult History(int? id)
        {
            return View();
        }
    }
}
