// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: ALL TEAM
// Module: Clarifications, Notifications & Audit Trail
// Purpose: MVC endpoints for the Clarifications pages of the shared signed-in pages.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    public class ClarificationsController : Controller
    {
        // Clarifications — Owner: ALL TEAM · Trello: Clarifications, Notifications & Audit Trail (https://trello.com/c/N1OFBdNM)
        // GET /Clarifications. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Clarification Thread — Owner: ALL TEAM · Trello: Clarifications, Notifications & Audit Trail (https://trello.com/c/N1OFBdNM)
        // GET /Clarifications/Details/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Details(int? id)
        {
            return View();
        }
    }
}
