using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
namespace Orange_Tendering_System.Areas.Admin.Controllers;
[Area("Admin"),Authorize(Roles=SystemRoles.Admin),AutoValidateAntiforgeryToken]
public class UsersController(ApplicationDbContext db,UserManager<ApplicationUser> users,EmployeeProvisioning provision):Controller
{
 public async Task<IActionResult> Index()=>View(await db.Users.AsNoTracking().OrderBy(u=>u.Email).ToListAsync());
 private async Task Departments(){ViewBag.Departments=await db.Departments.Where(d=>d.IsActive).ToListAsync();}
 public async Task<IActionResult> Create(){await Departments();return View();}
 [HttpPost] public async Task<IActionResult> Create(string fullName,string email,int departmentId,string role)
 {
  if(ModelState.IsValid) try { var result=await provision.CreateAsync(HttpContext,fullName,email,departmentId,[role]);if(result.Succeeded)return RedirectToAction(nameof(Index));foreach(var e in result.Errors)ModelState.AddModelError("",e.Description); }
  catch(Exception e) when(e is System.Net.Mail.SmtpException or InvalidOperationException){ModelState.AddModelError("","Account was not created: secure activation email delivery is unavailable. Configure SMTP and retry.");}
  await Departments();return View();
 }
 public async Task<IActionResult> Details(string id){var user=await users.FindByIdAsync(id);return user==null?NotFound():View(user);}
 public Task<IActionResult> Edit(string id)=>Details(id);
 [HttpPost] public async Task<IActionResult> SetActive(string id,bool active)
 {
  if(!ModelState.IsValid)return BadRequest("Invalid account state.");
  var user=await users.FindByIdAsync(id);if(user==null)return NotFound();
  if(!active && user.Id==users.GetUserId(User))return BadRequest("You cannot deactivate your own administrator account.");
  await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
  user.IsActive=active;var result=await users.UpdateAsync(user);if(!result.Succeeded)return BadRequest("Account state update failed.");
  await users.UpdateSecurityStampAsync(user);db.AuditLogs.Add(new AuditLog{UserId=users.GetUserId(User),EntityName="ApplicationUser",EntityId=id,Action=active?"UserActivated":"UserDeactivated",CreatedAt=DateTime.UtcNow});
  await db.SaveChangesAsync();await tx.CommitAsync();return RedirectToAction(nameof(Details),new{id});
 }
}
