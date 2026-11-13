// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Saja
// Module: Supplier Portal
// Purpose: MVC endpoints for the Dashboard pages of the Supplier workspace.
// Status: Supplier-only data and workflow, with server-side ownership checks.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;
using Orange_Tendering_System.Services;
using System.Security.Claims;

namespace Orange_Tendering_System.Areas.Supplier.Controllers
{
    [Area("Supplier")]
    [Authorize(Roles = SystemRoles.Supplier)]
    public class DashboardController(SupplierDashboardService dashboard) : Controller
    {
        // Supplier Dashboard — Owner: Saja · No Trello card
        // GET /Supplier. Ownership is checked in the supplier service.
        public async Task<IActionResult> Index()
        {
            var model = await dashboard.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (model == null) return Forbid();
            return View(model);
        }
    }
}
