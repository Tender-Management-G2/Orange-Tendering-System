
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
    public class UsersController : Controller
    {
        private readonly AdminService _adminService;
        private readonly UserManager<ApplicationUser> _userManager;

        public UsersController(
            AdminService adminService,
            UserManager<ApplicationUser> userManager)
        {
            _adminService = adminService;
            _userManager = userManager;
        }


        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? roleFilter,
            string? departmentFilter,
            string? statusFilter,
            int page = 1)
        {
            var model = await _adminService.GetUsersAsync(
                search,
                roleFilter,
                departmentFilter,
                statusFilter,
                page);

            return View(model);
        }

        // GET: /Admin/Users/Create
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var model = new CreateUserViewModel();

            await _adminService.FillCreateUserOptionsAsync(model);

            return View(model);
        }

        // POST: /Admin/Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            CreateUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await _adminService.FillCreateUserOptionsAsync(model);
                return View(model);
            }

            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(adminId))
            {
                return Challenge();
            }

            var result = await _adminService.CreateUserAsync(
                model, adminId);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        error.Description);
                }

                await _adminService.FillCreateUserOptionsAsync(model);
                return View(model);
            }

            TempData["SuccessMessage"] =
                "User created successfully. The active account can now sign in.";

            return RedirectToAction(nameof(Index));
        }



        // ==========================================
        // GET: /Admin/Users/Edit/{id}
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> Edit(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var model = await _adminService
                .GetUserForEditAsync(id);

            if (model == null)
                return NotFound();

            return View(model);
        }


        // ==========================================
        // POST: /Admin/Users/Edit/{id}
        // ==========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            string id,
            EditUserViewModel model)
        {
            if (id != model.Id)
                return BadRequest();

            // Validate submitted form
            if (!ModelState.IsValid)
            {
                var original = await _adminService
                    .GetUserForEditAsync(id);

                if (original == null)
                    return NotFound();

                model.Departments = original.Departments;
                model.AvailableRoles = original.AvailableRoles;
                model.CurrentRoles = original.CurrentRoles;
                model.Email = original.Email;

                return View(model);
            }

            // Get current Admin ID
            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrWhiteSpace(adminId))
                return Challenge();
        

            // Update account and single role
            var result = await _adminService.UpdateUserAsync(
                model, adminId);

            if (result == "NotFound")
                return NotFound();

            // Handle service validation and save errors
            if (result != "Success")
            {
                var messages = new Dictionary<string, string>
                {
                    ["SupplierAccount"] =
                        "Supplier accounts must be managed through Supplier Verification.",

                    ["InvalidName"] =
                        "Full name must contain 2 to 150 characters.",

                    ["InvalidDepartment"] =
                        "Please select a valid active department.",

                    ["DepartmentRequired"] =
                        "A department is required for Requesting Department users.",

                    ["InvalidRole"] =
                        "Please select a valid user role.",

                    ["SelfDeactivate"] =
                        "You cannot deactivate your own account.",

                    ["SelfRoleChange"] =
                        "You cannot remove your own Admin role.",

                    ["LastAdmin"] =
                        "You cannot remove or deactivate the last active Admin.",

                    ["RoleUpdateFailed"] =
                        "Unable to update the user's role.",

                    ["UpdateFailed"] =
                        "Unable to update the account."
                };

                var message = messages.TryGetValue(
                    result, out var errorMessage)
                        ? errorMessage
                        : "Unable to update the account.";

                // Display role errors next to Role dropdown
                if (result == "InvalidRole")
                {
                    ModelState.AddModelError(
                        nameof(model.SelectedRole),
                        message);
                }
                else
                {
                    ModelState.AddModelError("", message);
                }

                // Reload dropdown data
                var original = await _adminService
                    .GetUserForEditAsync(id);

                if (original == null)
                    return NotFound();

                model.Departments = original.Departments;
                model.AvailableRoles = original.AvailableRoles;
                model.CurrentRoles = original.CurrentRoles;
                model.Email = original.Email;

                return View(model);
            }

            TempData["SuccessMessage"] =
                "User information and role updated successfully.";

            return RedirectToAction(nameof(Index));
        }




        [HttpGet]
        public async Task<IActionResult> Details(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var user = await _adminService.GetUserDetailsAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

    }

}
