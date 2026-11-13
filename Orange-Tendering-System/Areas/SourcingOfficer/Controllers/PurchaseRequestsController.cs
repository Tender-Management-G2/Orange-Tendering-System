using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;
[Area("SourcingOfficer"),Authorize(Roles=SystemRoles.SourcingOfficer),AutoValidateAntiforgeryToken]
public class PurchaseRequestsController(PurchaseRequestService service):Controller
{
 public async Task<IActionResult> Index()=>View(await service.List(User,true));
 public async Task<IActionResult> Review(int id)
 { try { return View(await service.Read(id,User,true)); } catch(WorkflowException) { return NotFound(); } }
 [HttpPost] public async Task<IActionResult> Review(int id,ReviewDecision decision,string comment)
 { try { if(!ModelState.IsValid) throw new WorkflowException("Invalid decision.");await service.Decide(id,decision,comment,User);return RedirectToAction(nameof(Review),new{id}); }
 catch(WorkflowException e){ModelState.AddModelError("",e.Message);return View(await service.Read(id,User,true));} }
}
