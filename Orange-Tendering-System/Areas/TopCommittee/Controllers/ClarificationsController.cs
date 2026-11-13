using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = SystemRoles.TopCommittee)]
public sealed class ClarificationsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    ILogger<ClarificationsController> logger) : TopCommitteeControllerBase(db, users)
{
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Ask(QuestionInput input) => Process(input.RoundId, input, null, false);
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Reply(ReplyInput input) => Process(input.RoundId, null, input, false);
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Resolve(int roundId, int threadId) => Process(roundId, null, new ReplyInput { ThreadId = threadId }, true);
    private async Task<IActionResult> Process(int roundId, QuestionInput? question, ReplyInput? reply, bool resolve)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        if (await Committee.Load(user.Id, roundId) is null) return NotFound();
        if (!ModelState.IsValid) TempData["TopError"] = "Enter a subject (up to 200 characters) and message (up to 10,000 characters).";
        else
        {
            try
            {
                await Committee.Clarify(user.Id, roundId, question, reply, resolve);
                TempData["TopMessage"] = resolve ? "Clarification resolved." : "Clarification saved and the responsible Manager notified.";
            }
            catch (InvalidOperationException exception) { TempData["TopError"] = exception.Message; }
            catch (Exception exception) when (exception is DbUpdateException or System.Data.Common.DbException)
            { logger.LogWarning(exception, "Clarification could not be saved"); TempData["TopError"] = "The operation was not committed. Refresh and try again."; }
        }
        return RedirectToAction("ReviewEvaluation", "Decisions", new { id = roundId });
    }
}
