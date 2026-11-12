// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Haya
// Module: Sourcing Manager Review
// Purpose: MVC endpoints for the Reports pages of the Sourcing Manager workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.SourcingManager.Controllers
{
    [Area("SourcingManager")]
    [Authorize(Roles = SystemRoles.SourcingManager)]
    public class ReportsController : Controller
    {
        // Evaluation Reports List — Owner: Haya · Trello: Evaluation Reports List (https://trello.com/c/CWgcYFbk)
        // GET /SourcingManager/Reports. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Review Unified Evaluation Report — Owner: Haya · Trello: Review Unified Evaluation Report (https://trello.com/c/IKQ7sZwo)
        // GET /SourcingManager/Reports/Review/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Review(int? id)
        {
            return View();
        }

        // Approve Evaluation Report — Owner: Haya · Trello: Approve Evaluation Report (https://trello.com/c/tpvo9nZx)
        // GET /SourcingManager/Reports/Approve/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Approve(int? id)
        {
            return View();
        }

        // Reject Evaluation Report — Owner: Haya · Trello: Reject Evaluation Report (https://trello.com/c/WE4m7Kej)
        // GET /SourcingManager/Reports/Reject/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Reject(int? id)
        {
            return View();
        }

        // Return Report for Review — Owner: Haya · Trello: Return Report for Review (https://trello.com/c/r1oVUbDr)
        // GET /SourcingManager/Reports/Return/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Return(int? id)
        {
            return View();
        }

        // Forward to Top Committee — Owner: Haya · Trello: Forward to Top Committee (https://trello.com/c/953NoZV0)
        // GET /SourcingManager/Reports/Forward/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Forward(int? id)
        {
            return View();
        }

        // Sourcing Manager Decision History — Owner: Haya · Trello: Sourcing Manager Decision History (https://trello.com/c/CSb7lOgK)
        // GET /SourcingManager/Reports/History. TODO: query the data in a service and pass a view model.
        public IActionResult History()
        {
            return View();
        }
    }
}
