using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
namespace Orange_Tendering_System.Areas.Department.Controllers;
[Area("Department"), Authorize(Roles=SystemRoles.RequestingDepartment), AutoValidateAntiforgeryToken]
public class PurchaseRequestsController(PurchaseRequestService service) : Controller
{
 public async Task<IActionResult> Index()=>View(await service.List(User,false));
 public IActionResult Create()=>View();
 [HttpPost] public async Task<IActionResult> Create(string title,string justification,string itemName,decimal quantity,string unit,string specifications="")
 { try { if(!ModelState.IsValid) throw new WorkflowException("Invalid request."); var id=await service.Submit(title,justification,itemName,quantity,unit,specifications,User);return RedirectToAction(nameof(Details),new{id}); }
 catch(WorkflowException e){ModelState.AddModelError("",e.Message);return View();} }
 public async Task<IActionResult> Details(int id)
 { try { return View(await service.Read(id,User,false)); } catch(WorkflowException) { return NotFound(); } }
 public IActionResult Edit(int id)=>RedirectToAction(nameof(Details),new{id});
}
