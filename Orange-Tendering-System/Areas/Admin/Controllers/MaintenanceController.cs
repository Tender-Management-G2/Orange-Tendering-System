using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = SystemRoles.Admin)]
public sealed class MaintenanceController(AdminService admin, UserManager<ApplicationUser> users) : Controller
{
    [HttpGet]
    public IActionResult Create() => View("Form", new AdminMaintenanceForm { ScheduledDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Amman").DateTime), ResponsibleTeam = "Technical team" });
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await admin.GetMaintenanceAsync(id);
        return model == null ? NotFound() : View("Form", model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Create(AdminMaintenanceForm input) => input.Id != 0 ? Task.FromResult<IActionResult>(BadRequest()) : Save(input);
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Edit(int id, AdminMaintenanceForm input) => id != input.Id || id <= 0 ? Task.FromResult<IActionResult>(BadRequest()) : Save(input);
    private async Task<IActionResult> Save(AdminMaintenanceForm input)
    {
        if (ModelState.IsValid)
        {
            var actorId = users.GetUserId(User);
            if (actorId == null) return Challenge();
            try { await admin.SaveMaintenanceAsync(input, actorId); TempData["SuccessMessage"] = "Maintenance record saved."; return RedirectToAction("Index", "Analytics", new { area = "" }); }
            catch (WorkflowException error) { ModelState.AddModelError("", error.Message); }
        }
        return View("Form", input);
    }
}
