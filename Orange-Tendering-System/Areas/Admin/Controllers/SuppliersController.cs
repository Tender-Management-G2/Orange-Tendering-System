using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
namespace Orange_Tendering_System.Areas.Admin.Controllers;
[Area("Admin"),Authorize(Roles=SystemRoles.Admin),AutoValidateAntiforgeryToken]
public class SuppliersController(SupplierReviewService registrations) : Controller
{
    public async Task<IActionResult> Index()=>View(await registrations.List(User));
    public async Task<IActionResult> Review(int id)=>View(await registrations.Read(id,User));
    public IActionResult Details(int id)=>RedirectToAction(nameof(Review),new { id });
    [HttpPost] public async Task<IActionResult> Review(int id,bool approve,string notes)
    {
        try { if(!ModelState.IsValid) throw new WorkflowException("Invalid supplier decision."); await registrations.Decide(id,approve,notes,User); return RedirectToAction(nameof(Review),new { id }); }
        catch(WorkflowException ex) { ModelState.AddModelError("",ex.Message); return View(await registrations.Read(id,User)); }
    }
    public async Task<IActionResult> Document(int id)
    { var file=await registrations.Document(id,User); Response.Headers.CacheControl="no-store"; Response.Headers["X-Content-Type-Options"]="nosniff"; return PhysicalFile(file.Path,"application/octet-stream",file.Name); }
}
