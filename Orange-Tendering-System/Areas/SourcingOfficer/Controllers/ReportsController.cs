using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;
[Area("SourcingOfficer"), Authorize(Roles=SystemRoles.SourcingOfficer)]
public class ReportsController(TenderWorkflowService workflow) : ReportControllerBase(workflow)
{
    public IActionResult Comparison(int? id)=>RedirectToAction("Index","Workflow");
    public IActionResult Details(int? id)=>RedirectToAction("Index","Workflow");
    public IActionResult EvaluationResult(int? id)=>RedirectToAction("Index","Workflow");
    public IActionResult Returned(int? id)=>RedirectToAction("Index","Workflow");
    public IActionResult History(int? id)=>RedirectToAction("Index","Workflow");
    [HttpGet] public IActionResult SubmitToManager(int? id)=>RedirectToAction("Index","Workflow");
    [HttpPost] public Task<IActionResult> Forward(int id)=>Execute(id,async()=>
    {
        var report=await Workflow.ReviewReport(id,User);
        TenderWorkflowService.Require(report.IsCurrent,"Only the current finalized report can be forwarded.");
        await Workflow.Forward(report.Tender.Id,User);
    });
}
