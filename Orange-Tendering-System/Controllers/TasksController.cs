// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Shared workflow
// Purpose: My Tasks — the actionable work items of the signed-in internal role, computed from SQL Server by InternalDashboardService.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    public class TasksController(InternalDashboardService dashboard) : Controller
    {
        public async Task<IActionResult> Index()
        {
            var workspace = WorkspaceNavigation.Workspaces.FirstOrDefault(w => User.IsInRole(w.Role) && w.Area != "Supplier");
            return View(workspace == null ? [] : await dashboard.TasksAsync(workspace.Role, User));
        }
    }
}
