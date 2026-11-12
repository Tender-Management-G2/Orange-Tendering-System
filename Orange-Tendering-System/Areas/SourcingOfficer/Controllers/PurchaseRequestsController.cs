// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Shared — Amal (Index); Doulat (Review)
// Module: Sourcing Officer / Tender Management / Department / Purchase Requests (Sourcing Officer review step)
// Purpose: MVC endpoints for the PurchaseRequests pages of the Sourcing Officer workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers
{
    [Area("SourcingOfficer")]
    [Authorize(Roles = SystemRoles.SourcingOfficer)]
    public class PurchaseRequestsController : Controller
    {
        // Approved Purchase Requests — Owner: Amal · Trello: Approved Purchase Requests (https://trello.com/c/bsQzN4ih)
        // GET /SourcingOfficer/PurchaseRequests. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Purchase Request Review / Approval — Owner: Doulat · Trello: Purchase Request Review / Approval (https://trello.com/c/cOYVH9co)
        // GET /SourcingOfficer/PurchaseRequests/Review/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Review(int? id)
        {
            return View();
        }
    }
}
