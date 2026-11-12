// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: UNASSIGNED — Team assignment required
// Module: Top Committee
// Purpose: MVC endpoints for the Statistics pages of the Top Committee workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers
{
    [Area("TopCommittee")]
    [Authorize(Roles = SystemRoles.TopCommittee)]
    public class StatisticsController : Controller
    {
        // Management Statistics — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /TopCommittee/Statistics. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }
    }
}
