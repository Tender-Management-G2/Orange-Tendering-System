// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Doulat
// Module: Top Committee
// Purpose: MVC endpoints for the Dashboard pages of the Top Committee workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers
{
    [Area("TopCommittee")]
    [Authorize(Roles = SystemRoles.TopCommittee)]
    public class DashboardController : Controller
    {
        // Top Committee Dashboard — Owner: Doulat · Trello: Top Committee Dashboard
        // GET /TopCommittee. TODO: build the view model from real data in a service.
        public IActionResult Index()
        {
            return View(new WorkspaceDashboardViewModel());
        }
    }
}
