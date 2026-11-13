using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
namespace Orange_Tendering_System.Areas.EvaluationCommittee.Controllers;
[Area("EvaluationCommittee"), Authorize(Roles=SystemRoles.EvaluationCommittee+","+SystemRoles.SourcingManager)]
public class ReportsController(TenderWorkflowService workflow) : ReportControllerBase(workflow)
{
    public IActionResult Unified(int? id)=>RedirectToAction("Index", "Workflow");
    [HttpPost] public Task<IActionResult> Sign(int id,string documentHash,bool confirmed,string comment="") => Execute(id,async()=>
    {
        var report=await Workflow.ReviewReport(id,User);
        TenderWorkflowService.Require(report.IsCurrent,"An obsolete report cannot be signed.");
        await Workflow.FinalizeReport(report.Tender.Id,comment,User,documentHash,confirmed);
    });
}
