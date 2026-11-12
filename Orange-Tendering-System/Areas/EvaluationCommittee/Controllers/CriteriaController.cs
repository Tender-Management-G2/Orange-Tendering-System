// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Amal
// Module: Sourcing Officer / Tender Management (criteria screen used by the Evaluation Committee)
// Purpose: MVC endpoints for the Criteria pages of the Evaluation Committee workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.EvaluationCommittee.Controllers
{
    [Area("EvaluationCommittee")]
    [Authorize(Roles = SystemRoles.EvaluationCommittee)]
    public class CriteriaController : Controller
    {
        // Evaluation Criteria & Weights — Owner: Amal · Trello: Evaluation Criteria & Weights (https://trello.com/c/ruVwkBHz)
        // GET /EvaluationCommittee/Criteria/Index/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Index(int? id)
        {
            return View();
        }
    }
}
