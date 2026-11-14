using Orange_Tendering_System.Areas.TopCommittee.Services;
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
[PrivateFormFeedback]
public sealed class ClarificationsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    ILogger<ClarificationsController> logger, IWebHostEnvironment environment) : TopCommitteeControllerBase(db, users)
{
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(12*1024*1024)]
    [RequestFormLimits(MultipartBodyLengthLimit=12*1024*1024)]
    public async Task<IActionResult> Ask(PrivateQuestionInput input,CancellationToken cancellationToken)
    {
        var user=await ActiveUser(); if(user is null) return Forbid();
        var details=await Committee.Load(user.Id,input.RoundId); if(details is null) return NotFound();
        var store=new ClarificationFileStore(environment.ContentRootPath); StoredClarificationFile? file=null;
        try
        {
            if(!ModelState.IsValid) throw new InvalidOperationException("Enter the required subject and detailed message within the allowed length.");
            if(!details.CanClarify) throw new InvalidOperationException("This round is read-only or has no verified responsible Manager.");
            file=await store.Save(input.Attachment,cancellationToken);
            var result=await new PrivateClarificationService(Db,Users).Send(user.Id,input.RoundId,null,input.Subject,input.Message,input.SubmissionId,file);
            if(result.Duplicate) await ClarificationUploadCleanup.UnlessSaved(Db,store,file,logger);
            TempData["TopMessage"]=result.Duplicate?"This request was already sent. No duplicate was created.":"Private message sent to the responsible Sourcing Manager.";
            return RedirectToAction("ReviewEvaluation","Decisions",new{id=input.RoundId},"clarifications");
        }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateException or System.Data.Common.DbException or IOException or UnauthorizedAccessException)
        {
            await ClarificationUploadCleanup.UnlessSaved(Db,store,file,logger);
            if(e is not InvalidOperationException) logger.LogWarning(e,"Private clarification could not be saved");
            ViewData["ClarificationError"]=e is InvalidOperationException?e.Message:"The message could not be saved. Please try again.";
            ViewData["ClarificationSubject"]=input.Subject; ViewData["ClarificationMessage"]=input.Message;
            Response.StatusCode=400;
            return View("/Areas/TopCommittee/Views/Decisions/ReviewEvaluation.cshtml",details);
        }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Reply() => RedirectToActionPreserveMethod("Reply", "Conversations");
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
