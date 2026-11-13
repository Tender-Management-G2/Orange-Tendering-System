// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Rjoub
// Module: Administration
// Purpose: MVC endpoints for the Roles pages of the Admin workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.

// Role & Permission Management — Owner: Rjoub · Trello: Role & Permission Management (https://trello.com/c/eyB6myRo)
// GET /Admin/Roles. TODO: query the data in a service and pass a view model.




using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SystemRoles.Admin)]
    public class RolesController : Controller
    {
        private readonly AdminService _adminService;
        private readonly UserManager<ApplicationUser> _userManager;

        public RolesController(
            AdminService adminService,
            UserManager<ApplicationUser> userManager)
        {
            _adminService = adminService;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var roles = await _adminService.GetRolesAsync();
            var options = await _adminService
                .GetRoleMembershipOptionsAsync();

            ViewBag.Users = options.Users;
            ViewBag.AvailableRoles = options.Roles;

            return View(roles);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(
            RoleMembershipViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] =
                    "Please select a user and a role.";

                return RedirectToAction(nameof(Index));
            }

            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(adminId))
                return Challenge();

            var result = await _adminService.AssignRoleAsync(
                model.UserId,
                model.RoleName,
                adminId);

            TempData[result == "Success"
                ? "SuccessMessage"
                : "ErrorMessage"] = GetResultMessage(result);

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(
            RoleMembershipViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] =
                    "Please select a user and a role.";

                return RedirectToAction(nameof(Index));
            }

            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(adminId))
                return Challenge();

            var result = await _adminService.RemoveRoleAsync(
                model.UserId,
                model.RoleName,
                adminId);

            TempData[result == "Success"
                ? "SuccessMessage"
                : "ErrorMessage"] = GetResultMessage(result);

            return RedirectToAction(nameof(Index));
        }

        private static string GetResultMessage(string result)
        {
            return result switch
            {
                "Success" => "Role membership updated successfully.",
                "UserNotFound" => "User was not found.",
                "SupplierAccount" =>
                    "Supplier accounts are managed separately.",
                "InvalidRole" => "The selected role is invalid.",
                "AlreadyAssigned" =>
                    "This user already has the selected role.",
                "NotAssigned" =>
                    "This user does not have the selected role.",
                "DepartmentRequired" =>
                    "A department is required for this role.",
                "SelfAdminRemoval" =>
                    "You cannot remove your own Admin role.",
                "LastAdmin" =>
                    "You cannot remove the last active Admin.",
                _ => "Unable to update role membership."
            };
        }
    }
}

