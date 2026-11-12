// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Rjoub
// Module: Administration
// Purpose: MVC endpoints for the AuditLogs pages of the Admin workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SystemRoles.Admin)]
    public class AuditLogsController : Controller
    {
        // Audit Logs — Owner: Rjoub · Trello: Audit Logs (https://trello.com/c/eL9BCtiB)
        // GET /Admin/AuditLogs. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }
    }
}
