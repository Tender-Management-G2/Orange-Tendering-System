// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: UNASSIGNED — Team assignment required
// Module: Shared workflow / Voice Search
// Purpose: MVC endpoints for the Tasks pages of the shared signed-in pages.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    public class TasksController : Controller
    {
        // My Tasks — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /Tasks. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }
    }
}
