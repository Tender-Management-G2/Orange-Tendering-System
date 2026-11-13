// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Shared — UNASSIGNED — Team assignment required (Index); Tariq (Technical, Financial, Scores, AiAnalysis, History)
// Module: Technical & Financial Evaluation Committee
// Purpose: MVC endpoints for the Evaluations pages of the Evaluation Committee workspace.
// Compatibility GET routes: use the persisted, authorized weighted workflow.
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
        public IActionResult Index() => RedirectToAction("Index", "Workflow");

        // Technical Evaluation — Owner: Tariq · Trello: Technical Evaluation (https://trello.com/c/ocle0nNz)
        // GET /EvaluationCommittee/Evaluations/Technical/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Technical(int? id)
        {
            return RedirectToAction("Index", "Workflow");
        }

        // Financial Evaluation — Owner: Tariq · Trello: Financial Evaluation (https://trello.com/c/oqDGzKzR)
        // GET /EvaluationCommittee/Evaluations/Financial/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Financial(int? id)
        {
            return RedirectToAction("Index", "Workflow");
        }

        // Committee Scoring & Consolidation — Owner: Tariq · Trello: Committee Scoring & Consolidation (https://trello.com/c/zzYbN4wd)
        // GET /EvaluationCommittee/Evaluations/Scores/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Scores(int? id)
        {
            return RedirectToAction("Index", "Workflow");
        }

        // Ollama AI Bid Analysis — Owner: Tariq · Trello: Ollama AI Bid Analysis (https://trello.com/c/oWr6Ezhy)
        // GET /EvaluationCommittee/Evaluations/AiAnalysis/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult AiAnalysis(int? id)
        {
            return RedirectToAction("Index", "Workflow");
        }

        // Evaluation History — Owner: Tariq · Trello: Evaluation History (https://trello.com/c/gmOCr3ie)
        // GET /EvaluationCommittee/Evaluations/History/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult History(int? id)
        {
            return RedirectToAction("Index", "Workflow");
        }
    }
}
