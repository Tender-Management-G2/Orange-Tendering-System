// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Rjoub
// Module: Administration
// Purpose: MVC endpoints for the Dashboard pages of the Admin workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SystemRoles.Admin)]
    public class DashboardController : Controller
    {
        // Admin Dashboard — Owner: Rjoub · Trello: Admin Dashboard (https://trello.com/c/55BjFqvf)
        // GET /Admin. TODO: build the view model from real data in a service.
          private readonly AdminService _adminService;

            public DashboardController(AdminService adminService)
            {
                _adminService = adminService;
            }

            public async Task<IActionResult> Index()
            {
                var dashboard = await _adminService.GetDashboardAsync(); // استدعينا GetDashboardAsync() حتى نحصل على الإحصائيات الحقيقية.

            dashboard = new Orange_Tendering_System.ViewModels.WorkspaceDashboardViewModel
                {
                    DataConnected = dashboard.DataConnected,
                    DisplayName = User.Identity?.Name,  // عبّينا DisplayName من اسم المستخدم المسجل دخوله
                Metrics = dashboard.Metrics,
                    Trends = dashboard.Trends,
                    Tasks = dashboard.Tasks,
                    Activities = dashboard.Activities,
                    Invitations = dashboard.Invitations,
                    Statuses = dashboard.Statuses,
                    OngoingTender = dashboard.OngoingTender,
                    CurrentWorkflowStage = dashboard.CurrentWorkflowStage
                };

                return View(dashboard); // أرسلنا الـViewModel للـView باستخدام return View(dashboard)
        }
        }
    }
