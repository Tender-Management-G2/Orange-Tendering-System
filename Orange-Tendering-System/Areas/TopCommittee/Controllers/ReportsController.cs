using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;
<<<<<<< HEAD
[Area("TopCommittee"), Authorize(Roles=SystemRoles.TopCommittee)]
public class ReportsController(TenderWorkflowService workflow) : ReportControllerBase(workflow)
=======

[Area("TopCommittee"), Authorize(Roles = TopExecutiveRoles.Allowed)]
public sealed class ReportsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IWebHostEnvironment environment, IRazorViewEngine views) : TopCommitteeControllerBase(db, users)
>>>>>>> be32000 (temp)
{
    [HttpPost] public Task<IActionResult> Vote(int id,VoteDecision decision,string documentHash,bool confirmed,string comment,string? signatureImage=null)=>Execute(id,async()=>
    {
        var png=ReadSignature(signatureImage);
        var report=await Workflow.ReviewReport(id,User);
        TenderWorkflowService.Require(report.IsCurrent,"Only the current report can receive a final decision.");
        await Workflow.TopVote(report.Tender.Id,decision,comment,User,documentHash,confirmed,png);
    });
}
