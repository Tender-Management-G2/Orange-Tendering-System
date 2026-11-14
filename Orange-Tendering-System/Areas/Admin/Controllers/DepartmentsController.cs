
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
        public async Task<IActionResult> Index(
            string? search,
            string? statusFilter)
        {
            var model = await _adminService
                .GetDepartmentsAsync(search, statusFilter);

            return View(model);
        }

        // GET: /Admin/Departments/Create
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var model = new CreateDepartmentViewModel();

            await LoadCreateOptionsAsync(model);

            return View(model);
        }

        // POST: /Admin/Departments/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            CreateDepartmentViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await LoadCreateOptionsAsync(model);
                return View(model);
            }

            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(adminId))
                return Challenge();

            var result = await _adminService
                .CreateDepartmentAsync(model, adminId);

            if (result != "Success")
            {
                AddDepartmentError(result, nameof(model.Name));

                await LoadCreateOptionsAsync(model);

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
            var model = await _adminService
                .GetDepartmentForEditAsync(id);

            if (model == null)
                return NotFound();

            return View(model);
        }

        // POST: /Admin/Departments/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            EditDepartmentViewModel model)
        {
            if (id != model.Id)
                return BadRequest();

            if (!ModelState.IsValid)
            {
                await LoadEditOptionsAsync(model);
                return View(model);
            }

            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(adminId))
                return Challenge();

            var result = await _adminService
                .UpdateDepartmentAsync(model, adminId);

            if (result == "NotFound")
                return NotFound();

            if (result != "Success")
            {
                AddDepartmentError(result, nameof(model.Name));

                await LoadEditOptionsAsync(model);

                return View(model);
            }

            TempData["SuccessMessage"] =
                "Department updated successfully.";

            return RedirectToAction(nameof(Index));
        }

        // GET: /Admin/Departments/Details/5
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var model = await _adminService
                .GetDepartmentDetailsAsync(id);

            if (model == null)
                return NotFound();

            return View(model);
        }

        // Load eligible employees for Create
        private async Task LoadCreateOptionsAsync(
            CreateDepartmentViewModel model)
        {
            model.AvailableEmployees =
                await _adminService
                    .GetAvailableDepartmentEmployeesAsync();
        }

        // Load eligible employees for Edit
        private async Task LoadEditOptionsAsync(
            EditDepartmentViewModel model)
        {
            model.AvailableEmployees =
                await _adminService
                    .GetAvailableDepartmentEmployeesAsync(model.Id);
        }

        // Department validation messages
        private void AddDepartmentError(
            string result,
            string nameField)
        {
            switch (result)
            {
                case "Duplicate":
                    ModelState.AddModelError(
                        nameField,
                        "A department with this name already exists.");
                    break;

                case "InvalidName":
                    ModelState.AddModelError(
                        nameField,
                        "Department name must contain 2 to 100 characters.");
                    break;

                case "InvalidDescription":
                    ModelState.AddModelError(
                        nameof(CreateDepartmentViewModel.Description),
                        "Department description cannot exceed 500 characters.");
                    break;

                case "InvalidEmployees":
                    ModelState.AddModelError(
                        nameof(CreateDepartmentViewModel.SelectedEmployeeIds),
                        "One or more selected employees are unavailable or belong to another department.");
                    break;

                case "EmployeeRequiresDepartment":
                    ModelState.AddModelError(
                        nameof(CreateDepartmentViewModel.SelectedEmployeeIds),
                        "An employee with the Requesting Department role must stay assigned to a department. Change their role or department in Users first.");
                    break;

                default:
                    ModelState.AddModelError(
                        string.Empty,
                        "Unable to save department. Please try again.");
                    break;
            }
        }
    }
}
