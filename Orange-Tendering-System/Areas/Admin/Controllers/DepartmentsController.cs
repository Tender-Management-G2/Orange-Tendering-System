// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Rjoub
// Module: Administration
// Purpose: MVC endpoints for the Departments pages of the Admin workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.

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
    public class DepartmentsController : Controller
    {
        private readonly AdminService _adminService;
        private readonly UserManager<ApplicationUser> _userManager;

        public DepartmentsController(
            AdminService adminService,
            UserManager<ApplicationUser> userManager)
        {
            _adminService = adminService;
            _userManager = userManager;
        }

        // GET: /Admin/Departments
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var departments =
                await _adminService.GetDepartmentsAsync();

            return View(departments);
        }

        // GET: /Admin/Departments/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View(new CreateDepartmentViewModel());
        }

        // POST: /Admin/Departments/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            CreateDepartmentViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(adminId))
            {
                return Challenge();
            }

            var created = await _adminService
                .CreateDepartmentAsync(model, adminId);

            if (!created)
            {
                ModelState.AddModelError(
                    nameof(model.Name),
                    "A department with this name already exists.");

                return View(model);
            }

            TempData["SuccessMessage"] =
                "Department created successfully.";

            return RedirectToAction(nameof(Index));
        }


        // GET: /Admin/Departments/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var department =
                await _adminService.GetDepartmentForEditAsync(id);

            if (department == null)
            {
                return NotFound();
            }

            return View(department);
        }


        // POST: /Admin/Departments/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            EditDepartmentViewModel model)
        {
            if (id != model.Id)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(adminId))
            {
                return Challenge();
            }

            var result = await _adminService
                .UpdateDepartmentAsync(model, adminId);

            if (result == "NotFound")
            {
                return NotFound();
            }

            if (result == "Duplicate")
            {
                ModelState.AddModelError(
                    nameof(model.Name),
                    "A department with this name already exists.");

                return View(model);
            }

            if (result == "InvalidName")
            {
                ModelState.AddModelError(
                    nameof(model.Name),
                    "Department name must contain 2 to 100 characters.");

                return View(model);
            }

            TempData["SuccessMessage"] =
                "Department updated successfully.";

            return RedirectToAction(nameof(Index));
        }

    }
}
