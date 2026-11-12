// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Doulat
// Module: Top Committee
// Purpose: MVC endpoints for the Decisions pages of the Top Committee workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers
{
    [Area("TopCommittee")]
    [Authorize(Roles = SystemRoles.TopCommittee)]
    public class DecisionsController : Controller
    {
        // Review Tender Evaluation — Owner: Doulat · Trello: Review Tender Evaluation
        // GET /TopCommittee/Decisions/ReviewEvaluation/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult ReviewEvaluation(int? id)
        {
            return View();
        }

        // Committee Decision — Owner: Doulat · Trello: Committee Decision
        // GET /TopCommittee/Decisions/Decision/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Decision(int? id)
        {
            return View();
        }

        // Committee Voting & Decision History — Owner: Doulat · Trello: Committee Voting & Decision History
        // GET /TopCommittee/Decisions/History/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult History(int? id)
        {
            return View();
        }
    }
}
