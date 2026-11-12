// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: UNASSIGNED — Team assignment required
// Module: Supplier Portal
// Purpose: MVC endpoints for the Offers pages of the Supplier workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.Supplier.Controllers
{
    [Area("Supplier")]
    [Authorize(Roles = SystemRoles.Supplier)]
    public class OffersController : Controller
    {
        // My Submitted Offers — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Supplier/Offers. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Create Supplier Offer — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Supplier/Offers/Create/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Create(int? id)
        {
            return View();
        }

        // Edit Draft Offer — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Supplier/Offers/Edit/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Edit(int? id)
        {
            return View();
        }

        // Offer Details — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Supplier/Offers/Details/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Details(int? id)
        {
            return View();
        }

        // Supplier Offer History — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Supplier/Offers/History/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult History(int? id)
        {
            return View();
        }
    }
}
