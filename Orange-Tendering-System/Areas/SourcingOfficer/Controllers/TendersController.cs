// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Shared — Amal (Index, Details, Edit, Create, SelectSuppliers, Timeline, Rounds); UNASSIGNED — Team assignment required (Committees)
// Module: Sourcing Officer / Tender Management / Committees (tender-specific membership)
// Purpose: MVC endpoints for the Tenders pages of the Sourcing Officer workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers
{
    [Area("SourcingOfficer")]
    [Authorize(Roles = SystemRoles.SourcingOfficer)]
    public class TendersController : Controller
    {
        // Manage Tenders — Owner: Amal · Trello: Manage Tenders (https://trello.com/c/e2e0QlHT)
        // GET /SourcingOfficer/Tenders. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Tender Details — Owner: Amal · Trello: Manage Tenders (https://trello.com/c/e2e0QlHT)
        // GET /SourcingOfficer/Tenders/Details/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Details(int? id)
        {
            return View();
        }

        // Edit Tender — Owner: Amal · Trello: Manage Tenders (https://trello.com/c/e2e0QlHT)
        // GET /SourcingOfficer/Tenders/Edit/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Edit(int? id)
        {
            return View();
        }

        // Committee Assignment — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /SourcingOfficer/Tenders/Committees/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Committees(int? id)
        {
            return View();
        }

        // Create Tender — Owner: Amal · Trello: Create Tender (https://trello.com/c/907F6ufP)
        // GET /SourcingOfficer/Tenders/Create/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Create(int? id)
        {
            return View();
        }

        // Supplier Selection — Owner: Amal · Trello: Supplier Selection (https://trello.com/c/CDiXqUwu)
        // GET /SourcingOfficer/Tenders/SelectSuppliers/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult SelectSuppliers(int? id)
        {
            return View();
        }

        // Tender Timeline & Closing — Owner: Amal · Trello: Tender Timeline & Closing (https://trello.com/c/zNRW9odq)
        // GET /SourcingOfficer/Tenders/Timeline/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Timeline(int? id)
        {
            return View();
        }

        // Tendering / Retendering Rounds — Owner: Amal · No Trello card
        // GET /SourcingOfficer/Tenders/Rounds/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Rounds(int? id)
        {
            return View();
        }
    }
}
