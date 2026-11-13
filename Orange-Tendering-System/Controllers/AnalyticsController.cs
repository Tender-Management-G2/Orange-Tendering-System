// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Reports, Analytics & Exports (spec section 7)
// Purpose: role-filtered reports computed from live SQL Server data (ReportingService), with Excel and PDF export of exactly what is shown.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers
{
    [Authorize(Roles = SystemRoles.Admin + "," + SystemRoles.SourcingOfficer + "," + SystemRoles.SourcingManager + "," + SystemRoles.TopCommittee)]
    public class AnalyticsController(ReportingService reports, ReportExport export) : Controller
    {
        // Who may open which report. Admin takes no part in evaluations; management statistics belong to the Top Committee.
        public static readonly Dictionary<string, string[]> Audience = new()
        {
            ["purchase-requests"] = [SystemRoles.Admin, SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
            ["tenders"] = [SystemRoles.Admin, SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
            ["suppliers"] = [SystemRoles.Admin, SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
            ["evaluations"] = [SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
            ["governance"] = [SystemRoles.Admin, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
            ["management"] = [SystemRoles.TopCommittee]
        };

        private bool Allowed(string key) => Audience.TryGetValue(key, out var roles) && roles.Any(User.IsInRole);

        public IActionResult Index() => View(ReportingService.Catalog.Where(c => Allowed(c.Key)).ToList());

        public Task<IActionResult> PurchaseRequests() => Show("purchase-requests");
        public Task<IActionResult> Tenders() => Show("tenders");
        public Task<IActionResult> Suppliers() => Show("suppliers");
        public Task<IActionResult> Evaluations() => Show("evaluations");
        public Task<IActionResult> Governance() => Show("governance");
        public Task<IActionResult> Management() => Show("management");

        private async Task<IActionResult> Show(string key) => !Allowed(key) ? Forbid() : View("Report", await reports.BuildAsync(key));

        [HttpGet]
        public async Task<IActionResult> Export(string key, string format)
        {
            if (!Allowed(key ?? "")) return Forbid();
            var model = await reports.BuildAsync(key!);
            var name = $"{key}-{DateTime.UtcNow:yyyyMMdd-HHmm}";
            return format?.ToLowerInvariant() switch
            {
                "xlsx" => File(export.ToXlsx(model), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx"),
                "pdf" => File(export.ToPdf(model), "application/pdf", name + ".pdf"),
                _ => BadRequest("Use format=xlsx or format=pdf.")
            };
        }
    }
}
