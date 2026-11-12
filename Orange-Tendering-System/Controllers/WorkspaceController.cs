// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Ahmad
// Module: Authentication
// Purpose: MVC endpoints for the Workspace pages of the shared signed-in pages.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    public class WorkspaceController : Controller
    {
        // My Workspaces — Owner: Ahmad · Trello: User Login & Role Redirect (https://trello.com/c/WoTewqHd)
        // GET /Workspace. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }
    }
}
