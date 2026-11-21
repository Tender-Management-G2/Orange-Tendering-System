using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Controllers
{
    // JSON backend of the top-bar search box (internal workspaces). Results are role-scoped by InternalDashboardService.
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class SearchController(InternalDashboardService service, AdminService adminService) : Controller
    {
        [HttpGet]
        [Authorize(Roles = SystemRoles.Admin)]
        public async Task<IActionResult> AdminVoiceTerm(string? q, string? scope, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length > 100) return BadRequest();
            var term = await adminService.ResolveVoiceSearchTermAsync(q.Trim(), scope, cancellationToken);
            return Json(new { term });
        }

        [HttpGet]
        public async Task<IActionResult> Query(string? q)
        {
            if ((q?.Length ?? 0) > 100) return BadRequest(new { error = "Search text must not exceed 100 characters." });
            if (User.IsInRole(SystemRoles.Supplier))
                return Json(new { items = await service.SearchSupplierAsync(User, q ?? string.Empty) });
            var workspace = WorkspaceNavigation.Workspaces.FirstOrDefault(w => User.IsInRole(w.Role) && w.Area != "Supplier");
            if (workspace == null) return Json(new { items = Array.Empty<object>() });
            return Json(new { items = await service.SearchAsync(workspace.Role, User, q ?? string.Empty) });
        }
    }
}
