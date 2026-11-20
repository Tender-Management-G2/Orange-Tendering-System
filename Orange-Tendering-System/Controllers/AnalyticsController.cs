// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Reports, Analytics & Exports (spec section 7)
// Purpose: role-filtered reports computed from live SQL Server data (ReportingService), with Excel and PDF export of exactly what is shown.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;
using System.Security.Claims;

namespace Orange_Tendering_System.Controllers
{
    [Authorize(Roles = SystemRoles.Admin + "," + SystemRoles.SourcingOfficer + "," + SystemRoles.SourcingManager + "," + SystemRoles.TopCommittee)]
    public class AnalyticsController(ReportingService reports, ReportExport export, AdminService admin, TenderWorkflowService workflow, DocumentWatermarkService watermarks) : Controller
    {
        // Who may open which report. Admin takes no part in evaluations; management statistics belong to the Top Committee.
        public static readonly Dictionary<string, string[]> Audience = new()
        {
            ["analytics-dashboard"] = [SystemRoles.Admin],
            ["purchase-requests"] = [SystemRoles.Admin, SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
            ["tenders"] = [SystemRoles.Admin, SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
            ["suppliers"] = [SystemRoles.Admin, SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
            ["evaluations"] = [SystemRoles.Admin, SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
            ["governance"] = [SystemRoles.Admin, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
            ["management"] = [SystemRoles.Admin, SystemRoles.TopCommittee],
            ["reports-management"] = [SystemRoles.Admin]
        };

        private bool Allowed(string key) => Audience.TryGetValue(key, out var roles) && roles.Any(User.IsInRole);

        public async Task<IActionResult> Index()
        {
            var available = ReportingService.Catalog.Where(c => Allowed(c.Key)).ToList();
            if (!User.IsInRole(SystemRoles.Admin)) return View(available);
            return View("AdminLanding", new ReportsAnalyticsLandingViewModel
            {
                Dashboard = await reports.BuildAsync("analytics-dashboard"),
                Reports = available.Where(x => x.Key != "analytics-dashboard").ToList()
            });
        }

        [Authorize(Roles = SystemRoles.Admin)]
        public async Task<IActionResult> Business()
        {
            var model = await admin.GetBusinessOverviewAsync();
            var evaluation = await workflow.StandingCommittee("Evaluation", User);
            var top = await workflow.StandingCommittee("Top", User);
            model.EvaluationReadySeats = evaluation.Slots.Count(x => x.SelectedId != null && x.Candidates.Any(c => c.Value == x.SelectedId));
            model.TopReadySeats = top.Slots.Count(x => x.SelectedId != null && x.Candidates.Any(c => c.Value == x.SelectedId));
            return View("AdminOverview", model);
        }

        public async Task<IActionResult> PurchaseRequests([FromQuery] ReportFilters filters)
            => !Allowed("purchase-requests") ? Forbid() : View("PurchaseRequestReport", await reports.BuildAsync("purchase-requests", filters));
        [Authorize(Roles = SystemRoles.Admin)] public Task<IActionResult> Dashboard() => Show("analytics-dashboard");
        public async Task<IActionResult> Tenders([FromQuery] ReportFilters filters)
            => !Allowed("tenders") ? Forbid() : View("OperationalReport", await reports.BuildAsync("tenders", filters));
        public async Task<IActionResult> Suppliers([FromQuery] ReportFilters filters)
            => !Allowed("suppliers") ? Forbid() : View("SupplierReport", await reports.BuildAsync("suppliers", filters));
        public async Task<IActionResult> Evaluations([FromQuery] ReportFilters filters)
            => !Allowed("evaluations") ? Forbid() : View("OperationalReport", await reports.BuildAsync("evaluations", filters));
        public async Task<IActionResult> Governance([FromQuery] ReportFilters filters)
            => !Allowed("governance") ? Forbid() : View("GovernanceReport", await reports.BuildAsync("governance", filters));
        public async Task<IActionResult> Management([FromQuery] ReportFilters filters)
            => !Allowed("management") ? Forbid() : View("TopCommitteeReport", await reports.BuildAsync("management", filters));
        [Authorize(Roles = SystemRoles.Admin)] public async Task<IActionResult> ReportsManagement([FromQuery] ReportFilters filters)
            => View("ReportsManagement", await reports.BuildAsync("reports-management", filters));
        [Authorize(Roles = SystemRoles.Admin)]
        public async Task<IActionResult> ReportDetails(int id)
        {
            var model = await reports.ReportDetailsAsync(id);
            return model == null ? NotFound() : View("Report", model);
        }

        private Task<IActionResult> Show(string key) => Show(key, new ReportFilters());
        private async Task<IActionResult> Show(string key, ReportFilters filters) => !Allowed(key) ? Forbid() : View("Report", await reports.BuildAsync(key, filters));

        [HttpGet]
        public Task<IActionResult> Filter(string key, [FromQuery] ReportFilters filters) => Show(key, filters);

        [HttpGet]
        public async Task<IActionResult> Export(string key, string format, [FromQuery] ReportFilters filters)
        {
            if (!Allowed(key ?? "")) return Forbid();
            // Downloads contain the full filtered result set rather than only the visible table page.
            filters.ExportAll = true;
            filters.ExpiryPage = filters.PerformancePage = filters.ReportPage = 1;
            var model = await reports.BuildAsync(key!, filters);
            var name = $"{key}-{DateTime.UtcNow:yyyyMMdd-HHmm}";
            return format?.ToLowerInvariant() switch
            {
                "xlsx" => File(export.ToXlsx(model), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx"),
                "pdf" => File(await WatermarkedPdf(model), "application/pdf", name + ".pdf"),
                _ => BadRequest("Use format=xlsx or format=pdf.")
            };
        }

        private async Task<byte[]> WatermarkedPdf(ReportModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var identity = string.IsNullOrWhiteSpace(userId) ? "Authorized report viewer" : await watermarks.IdentityAsync(userId);
            return watermarks.Apply(export.ToPdf(model), new WatermarkStamp(identity, DateTime.UtcNow, "Reports Center", model.Title, "Downloaded"), underlay: true).Bytes;
        }
    }
}
