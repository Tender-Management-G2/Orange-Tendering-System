using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;
[Area("TopCommittee"),Authorize(Roles=SystemRoles.TopCommittee+","+SystemRoles.SourcingManager)]
[PrivateFormFeedback]
public sealed class ConversationsController(ApplicationDbContext db,UserManager<ApplicationUser> users,IWebHostEnvironment environment,ILogger<ConversationsController> logger) : TopCommitteeControllerBase(db,users)
{
    PrivateClarificationService Conversations=>new(Db,Users);
    public async Task<IActionResult> Index()
    { var user=await ActiveUser(); if(user is null) return Forbid(); Response.Headers.CacheControl="no-store"; return View(await Conversations.Inbox(user.Id)); }
    public async Task<IActionResult> Details(int id)
    { var user=await ActiveUser(); if(user is null) return Forbid(); var item=await Conversations.Read(user.Id,id); if(item is null) return NotFound(); Response.Headers.CacheControl="no-store"; return View(item); }
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(12*1024*1024)]
    [RequestFormLimits(MultipartBodyLengthLimit=12*1024*1024)]
    public async Task<IActionResult> Reply(PrivateReplyInput input,CancellationToken cancellationToken)
    {
        var user=await ActiveUser(); if(user is null) return Forbid(); var item=await Conversations.Read(user.Id,input.ThreadId); if(item is null) return NotFound();
        var store=new ClarificationFileStore(environment.ContentRootPath); StoredClarificationFile? file=null;
        try
        {
            if(!ModelState.IsValid) throw new InvalidOperationException("Enter a reply up to 10,000 characters.");
            if(!item.CanReply) throw new InvalidOperationException("This conversation is read-only.");
            file=await store.Save(input.Attachment,cancellationToken);
            var saved=await Conversations.Send(user.Id,item.Details.Round.Id,input.ThreadId,null,input.Message,input.SubmissionId,file);
            if(saved.Duplicate) await ClarificationUploadCleanup.UnlessSaved(Db,store,file,logger);
            TempData["TopMessage"]=saved.Duplicate?"This reply was already sent.":"Your private reply was sent.";
            return RedirectToAction(nameof(Details),new{id=input.ThreadId});
        }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateException or System.Data.Common.DbException or IOException or UnauthorizedAccessException)
        {
            await ClarificationUploadCleanup.UnlessSaved(Db,store,file,logger); if(e is not InvalidOperationException) logger.LogWarning(e,"Private reply could not be saved");
            ViewData["ClarificationError"]=e is InvalidOperationException?e.Message:"Your reply could not be saved. Try again."; ViewData["ClarificationMessage"]=input.Message;
            Response.StatusCode=400; return View("Details",item);
        }
    }
    public async Task<IActionResult> Download(int id)
    {
        var user=await ActiveUser(); if(user is null) return Forbid();
        var document=await Db.Documents.AsNoTracking().Where(d=>d.Id==id && d.DocumentType=="TopClarificationAttachment" && d.ClarificationMessageId!=null)
            .Select(d=>new {d.FileName,d.StoragePath,d.ContentType,ThreadId=d.ClarificationMessage!.ClarificationId}).SingleOrDefaultAsync();
        if(document is null || await Conversations.Read(user.Id,document.ThreadId) is null) return NotFound();
        var store=new ClarificationFileStore(environment.ContentRootPath); string path;
        try {path=store.PathFor(document.StoragePath);} catch(InvalidOperationException){return NotFound();}
        if(!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl="no-store"; Response.Headers["X-Content-Type-Options"]="nosniff";
        return PhysicalFile(path,document.ContentType,document.FileName);
    }
}
