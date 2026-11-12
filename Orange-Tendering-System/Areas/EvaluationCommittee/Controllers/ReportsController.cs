// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Tariq
// Module: Technical & Financial Evaluation Committee
// Purpose: MVC endpoints for the Reports pages of the Evaluation Committee workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.EvaluationCommittee.Controllers
{
    [Area("EvaluationCommittee")]
    [Authorize(Roles = SystemRoles.EvaluationCommittee)]
    public class ReportsController : Controller
    {
        // Unified Evaluation Report — Owner: Tariq · Trello: Unified Evaluation Report (https://trello.com/c/ssHM0e0R)
        // GET /EvaluationCommittee/Reports/Unified/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Unified(int? id)
        {
            return View();
        }
    }
}
