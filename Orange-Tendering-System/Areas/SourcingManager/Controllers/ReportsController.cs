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
    public IActionResult History()=>RedirectToAction("Index","Workflow");
    [HttpPost] public Task<IActionResult> Decision(int id,string decision,string documentHash,bool confirmed,string comment="")=>Execute(id,async()=>
    {
        var report=await Workflow.ReviewReport(id,User);
        TenderWorkflowService.Require(report.IsCurrent && decision is "Approve" or "Reject","Select an allowed decision on the current report. Use Return for Revision for corrections.");
        await Workflow.ManagerDecision(report.Tender.Id,decision,comment,User,documentHash,confirmed);
    });
}
