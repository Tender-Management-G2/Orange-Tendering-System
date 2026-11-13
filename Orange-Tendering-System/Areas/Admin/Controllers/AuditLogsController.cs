// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Rjoub
// Module: Administration
// Purpose: MVC endpoints for the AuditLogs pages of the Admin workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.

// Audit Logs — Owner: Rjoub · Trello: Audit Logs (https://trello.com/c/eL9BCtiB)
// GET /Admin/AuditLogs. TODO: query the data in a service and pass a view model.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.Admin.Controllers
{
    [Area("Admin")]  // [Area("Admin")]: حددنا إن الـController تابع للـAdmin.
    [Authorize(Roles = SystemRoles.Admin)] // منعنا باقي المستخدمين من الوصول
    public class AuditLogsController : Controller
    {
        private readonly AdminService _adminService; // AdminService: المسؤول عن جلب البيانات من قاعدة البيانات

        public AuditLogsController(AdminService adminService)
        {
            _adminService = adminService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
           string? search,
           string? actionFilter,
           DateTime? fromDate,
           DateTime? toDate,
           int page = 1)
        {
            if (fromDate.HasValue &&
                toDate.HasValue &&
                fromDate.Value.Date > toDate.Value.Date)
            {
                ModelState.AddModelError(
                    "",
                    "From Date cannot be later than To Date.");
            }

            var model = await _adminService.GetAuditLogsAsync(
                search,
                actionFilter,
                fromDate,
                toDate,
                page);

            return View(model);
        }
    }
}