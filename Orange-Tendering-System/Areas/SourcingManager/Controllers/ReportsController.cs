using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
namespace Orange_Tendering_System.Areas.SourcingManager.Controllers;
[Area("SourcingManager"), Authorize(Roles=SystemRoles.SourcingManager)]
public class ReportsController(TenderWorkflowService workflow) : ReportControllerBase(workflow)
{
    [HttpGet] public IActionResult Approve(int? id)=>RedirectToAction("Index","Workflow");
    [HttpGet] public IActionResult Reject(int? id)=>RedirectToAction("Index","Workflow");
    [HttpGet] public IActionResult Forward(int? id)=>RedirectToAction("Index","Workflow");
    // Pending Reports: tenders waiting for this manager's report review. Decision History: tenders already decided.
    public override async Task<IActionResult> Index()=>View("Index",(await Workflow.List(User)).Where(x=>x.Status==nameof(TenderStatus.ManagerReview)).ToList());
    public async Task<IActionResult> History()=>View("History",(await Workflow.List(User)).Where(x=>x.Status is nameof(TenderStatus.TopCommitteeReview) or nameof(TenderStatus.Awarded) or nameof(TenderStatus.Rejected) or nameof(TenderStatus.Cancelled)).ToList());
    [HttpPost] public Task<IActionResult> Decision(int id,string decision,string documentHash,bool confirmed,string comment="",string? signatureImage=null)=>Execute(id,async()=>
    {
        var png=decision=="Approve"?ReadSignature(signatureImage):null;
        var report=await Workflow.ReviewReport(id,User);
        TenderWorkflowService.Require(report.IsCurrent && decision is "Approve" or "Reject" or "Return","Select an allowed decision on the current report.");
        await Workflow.ManagerDecision(report.Tender.Id,decision,comment,User,documentHash,confirmed,png);
    });
}
