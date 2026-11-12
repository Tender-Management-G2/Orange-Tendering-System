// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Tariq
// Module: Technical & Financial Evaluation Committee
// Purpose: MVC endpoints for the Dashboard pages of the Evaluation Committee workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.EvaluationCommittee.Controllers
{
    [Area("EvaluationCommittee")]
    [Authorize(Roles = SystemRoles.EvaluationCommittee)]
    public class DashboardController : Controller
    {
        // Committee Dashboard — Owner: Tariq · Trello: Committee Dashboard
        // GET /EvaluationCommittee. TODO: build the view model from real data in a service.
        public IActionResult Index()
        {
            return View(new WorkspaceDashboardViewModel());
        }
    }
}
