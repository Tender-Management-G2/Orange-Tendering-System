// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Shared — UNASSIGNED — Team assignment required (Index); Tariq (Technical, Financial, Scores, AiAnalysis, History)
// Module: Technical & Financial Evaluation Committee
// Purpose: MVC endpoints for the Evaluations pages of the Evaluation Committee workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.EvaluationCommittee.Controllers
{
    [Area("EvaluationCommittee")]
    [Authorize(Roles = SystemRoles.EvaluationCommittee)]
    public class EvaluationsController : Controller
    {
        // Assigned Evaluations — Owner: UNASSIGNED — Team assignment required · No Trello card
        // GET /EvaluationCommittee/Evaluations. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Technical Evaluation — Owner: Tariq · Trello: Technical Evaluation
        // GET /EvaluationCommittee/Evaluations/Technical/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Technical(int? id)
        {
            return View();
        }

        // Financial Evaluation — Owner: Tariq · Trello: Financial Evaluation
        // GET /EvaluationCommittee/Evaluations/Financial/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Financial(int? id)
        {
            return View();
        }

        // Committee Scoring & Consolidation — Owner: Tariq · Trello: Committee Scoring & Consolidation
        // GET /EvaluationCommittee/Evaluations/Scores/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Scores(int? id)
        {
            return View();
        }

        // Ollama AI Bid Analysis — Owner: Tariq · Trello: Ollama AI Bid Analysis
        // GET /EvaluationCommittee/Evaluations/AiAnalysis/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult AiAnalysis(int? id)
        {
            return View();
        }

        // Evaluation History — Owner: Tariq · Trello: Evaluation History
        // GET /EvaluationCommittee/Evaluations/History/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult History(int? id)
        {
            return View();
        }
    }
}
