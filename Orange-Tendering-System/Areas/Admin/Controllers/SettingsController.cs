// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Rjoub
// Module: Administration
// Purpose: MVC endpoints for the Settings pages of the Admin workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.

// System Settings — Owner: Rjoub · Trello: System Settings (https://trello.com/c/26iJGjhv)
// GET /Admin/Settings. TODO: query the data in a service and pass a view model.


using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SystemRoles.Admin)]
    public class SettingsController : Controller
    {
        private readonly SettingsService _settingsService;

        public SettingsController(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        // GET: /Admin/Settings
        [HttpGet]
        public IActionResult Index()
        {
            var settings = _settingsService.GetSettings();

            return View(settings);
        }
    }
}
