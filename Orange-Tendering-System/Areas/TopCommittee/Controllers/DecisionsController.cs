// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Top Committee - final decision pages (spec 4.7): review of results, voting entry point and bid history.
// These pages are read-only. The vote and the signature are cast on the signed report page (Reports/Review -> TenderWorkflowService.TopVote),
// which enforces the Manager's prior signature, the committee signatures, one vote per member and the 3-of-4 approval rule.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers
{
    [Area("TopCommittee")]
    [Authorize(Roles = SystemRoles.TopCommittee)]
    public class DecisionsController(TopCommitteeService service) : Controller
    {
        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        public IActionResult Index() => RedirectToAction(nameof(ReviewEvaluation));

        // GET /TopCommittee/Decisions/ReviewEvaluation/{id?}: the tenders waiting for me, or one tender's full results.
        public async Task<IActionResult> ReviewEvaluation(int? id)
        {
            if (id == null) return View("Cases", (await service.Cases(UserId)).Where(c => c.Outcome == null).ToList());
            var details = await service.Details(UserId, id.Value);
            return details == null ? NotFound() : View(details);
        }

        // GET /TopCommittee/Decisions/Decision/{id?}: voting status of a tender and the entry to the signed report page.
        public async Task<IActionResult> Decision(int? id)
        {
            if (id == null) return View("Cases", (await service.Cases(UserId)).Where(c => c.AwaitingMyVote).ToList());
            var details = await service.Details(UserId, id.Value);
            return details == null ? NotFound() : View(details);
        }

        // GET /TopCommittee/Decisions/History: every tender I was assigned to with its outcome.
        public async Task<IActionResult> History(int? id)
        {
            if (id != null) return RedirectToAction(nameof(ReviewEvaluation), new { id });
            return View((await service.Cases(UserId)).Where(c => c.Outcome != null || c.MyVote != null).ToList());
        }
    }
}
