// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Doulat
// Module: Department / Purchase Requests
// Purpose: MVC endpoints for the PurchaseRequests pages of the Requesting Department workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.Department.Controllers
{
    [Area("Department")]
    [Authorize(Roles = SystemRoles.RequestingDepartment)]
    public class PurchaseRequestsController : Controller
    {
        // My Purchase Requests — Owner: Doulat · Trello: My Purchase Requests (https://trello.com/c/SjSwbYUx)
        // GET /Department/PurchaseRequests. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Create Purchase Request — Owner: Doulat · Trello: Create Purchase Request (https://trello.com/c/vCxUl1Bb)
        // GET /Department/PurchaseRequests/Create. TODO: query the data in a service and pass a view model.
        public IActionResult Create()
        {
            return View();
        }

        // Purchase Request Details & History — Owner: Doulat · Trello: Purchase Request Details & History (https://trello.com/c/IR3EpX2r)
        // GET /Department/PurchaseRequests/Details/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Details(int? id)
        {
            return View();
        }

        // Edit & Resubmit Purchase Request — Owner: Doulat · No Trello card
        // GET /Department/PurchaseRequests/Edit/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Edit(int? id)
        {
            return View();
        }
    }
}
