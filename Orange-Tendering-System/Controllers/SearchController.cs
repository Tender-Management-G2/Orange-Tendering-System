using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Controllers
{
    // JSON backend of the top-bar search box (internal workspaces). Results are role-scoped by InternalDashboardService.
    [Authorize]
    public class SearchController(InternalDashboardService service) : Controller
    {
        [HttpGet]
        public async Task<IActionResult> Query(string? q)
        {
            if (User.IsInRole(SystemRoles.Supplier))
                return Json(new { items = await service.SearchSupplierAsync(User, q ?? string.Empty) });
            var workspace = WorkspaceNavigation.Workspaces.FirstOrDefault(w => User.IsInRole(w.Role) && w.Area != "Supplier");
            if (workspace == null) return Json(new { items = Array.Empty<object>() });
            return Json(new { items = await service.SearchAsync(workspace.Role, User, q ?? string.Empty) });
        }
    }
}
