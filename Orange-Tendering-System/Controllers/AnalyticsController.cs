using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Controllers;

[Authorize(Roles = SystemRoles.Admin + "," + SystemRoles.SourcingOfficer + "," + SystemRoles.SourcingManager + "," + TopExecutiveRoles.Allowed)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AnalyticsController(ReportingService reports, ReportExport export, AdminService admin, TenderWorkflowService workflow,
    ApplicationDbContext db, AuditLogService audit, ReportAccessService access, FinalTenderContractService contracts) : Controller
{
    private bool IsAdmin => User.IsInRole(SystemRoles.Admin);
    private string ActorId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool CanUseReports => !User.IsInRole(SystemRoles.Supplier);

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AnalyticsFilter filter, CancellationToken ct)
    {
        if (!CanUseReports) return Forbid();
        if (!IsAdmin) return await Catalog(ct);
        if (!ModelState.IsValid) return BadRequest(ModelState);
        return View("Dashboard", new AnalyticsDashboardPage(await reports.FilterFormAsync(nameof(Index), filter, ct), await reports.DashboardAsync(filter, ct)));
    }

    [HttpGet]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        if (!CanUseReports) return Forbid();
        var allowed = await access.AvailableAsync(User, ct);
        return View("Index", ReportingService.Catalog.Where(r => allowed.Contains(r.Key)).ToList());
    }

    [HttpGet, Authorize(Roles = SystemRoles.Admin)]
    public async Task<IActionResult> DashboardData([FromQuery] AnalyticsFilter filter, CancellationToken ct)
    {
        if (!CanUseReports) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);
        return PartialView("_DashboardData", await reports.DashboardAsync(filter, ct));
    }

    [HttpGet, Authorize(Roles = SystemRoles.Admin)]
    public async Task<IActionResult> Maintenance()
    {
        var model = await admin.GetBusinessOverviewAsync();
        var evaluation = await workflow.StandingCommittee("Evaluation", User);
        var top = await workflow.StandingCommittee("Top", User);
        model.EvaluationReadySeats = evaluation.Slots.Count(x => x.SelectedId != null && x.Candidates.Any(c => c.Value == x.SelectedId));
        model.TopReadySeats = top.Slots.Count(x => x.SelectedId != null && x.Candidates.Any(c => c.Value == x.SelectedId));
        return View("AdminOverview", model);
    }

    [HttpGet]
    public Task<IActionResult> PurchaseRequests([FromQuery] AnalyticsFilter filter, int? table, int tablePage, CancellationToken ct, string? section = null) =>
        Show("purchase-requests", filter, section, table, tablePage, ct);
    [HttpGet]
    public Task<IActionResult> Tenders([FromQuery] AnalyticsFilter filter, int? table, int tablePage, CancellationToken ct, string? section = null) =>
        Show("tenders", filter, section, table, tablePage, ct);
    [HttpGet]
    public Task<IActionResult> Suppliers([FromQuery] AnalyticsFilter filter, int? table, int tablePage, CancellationToken ct, string? section = null) =>
        Show("suppliers", filter, section, table, tablePage, ct);
    [HttpGet]
    public Task<IActionResult> Evaluations([FromQuery] AnalyticsFilter filter, int? table, int tablePage, CancellationToken ct, string? section = null) =>
        Show("evaluations", filter, section, table, tablePage, ct);
    [HttpGet]
    public Task<IActionResult> Governance([FromQuery] AnalyticsFilter filter, int? table, int tablePage, CancellationToken ct, string? section = null) =>
        Show("governance", filter, section, table, tablePage, ct);
    [HttpGet]
    public Task<IActionResult> Management([FromQuery] AnalyticsFilter filter, int? table, int tablePage, CancellationToken ct, string? section = null) =>
        Show("management", filter, section, table, tablePage, ct);

    private async Task<IActionResult> Show(string key, AnalyticsFilter filter, string? section, int? table, int tablePage, CancellationToken ct)
    {
        var scope = await ReadScopeAsync(key, ct);
        if (scope == null) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var definition = ReportingService.Catalog.Single(r => r.Key == key);
        var model = await BuildForAuthorAsync(key, filter, scope, ct);
        if (model == null) return Forbid();
        var selected = model.Tables.FindIndex(t => t.Key == section);
        if (selected < 0 && table is { } legacy && legacy >= 0 && legacy < model.Tables.Count)
            selected = legacy;
        if (selected < 0)
        {
            var defaultSection = (key, filter.TenderScope) switch
            {
                ("tenders", "no-offers") => "no-offers",
                ("tenders", "evaluation-delays") => "evaluation-delays",
                _ => definition.DefaultSection
            };
            selected = Math.Max(0, model.Tables.FindIndex(t => t.Key == defaultSection));
        }
        var filters = await reports.FilterFormAsync(definition.Action, filter, ct);
        return View("Report", new AnalyticsReportPage(filters with { Section = model.Tables.ElementAtOrDefault(selected)?.Key },
            model, selected, tablePage));
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? key, string? format, [FromQuery] AnalyticsFilter filter, CancellationToken ct)
    {
        if (key == null) return Forbid();
        var scope = await ReadScopeAsync(key, ct);
        if (scope == null) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var extension = format?.ToLowerInvariant();
        if (extension is not ("xlsx" or "pdf")) return BadRequest("Use format=xlsx or format=pdf.");
        var model = await BuildForAuthorAsync(key, filter, scope, ct);
        if (model == null) return Forbid();
        var name = $"{key}-{model.GeneratedAt:yyyyMMdd-HHmm}.{extension}";
        return extension == "xlsx"
            ? File(export.ToXlsx(model), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name)
            : File(export.ToPdf(model), "application/pdf", name);
    }

    private async Task<ReportModel?> BuildForAuthorAsync(string key, AnalyticsFilter filter, AnalyticsReadScope scope, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().Where(u => u.Id == ActorId && u.IsActive)
            .Select(u => new { u.FullName, u.UserName }).SingleOrDefaultAsync(ct);
        if (actor == null) return null;
        var name = string.IsNullOrWhiteSpace(actor.FullName) ? actor.UserName?.Trim() : actor.FullName.Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;

        var roles = SystemRoles.PublicRoles.Where(User.IsInRole).ToArray();
        if (roles.Length == 0 && TopExecutiveRoles.IsMember(User)) roles = [SystemRoles.TopCommittee];
        var model = await reports.BuildAsync(key, filter, scope, ct);
        model.Header = new AnalyticsReportHeader(name, roles);
        return model;
    }

    private async Task<AnalyticsReadScope?> ReadScopeAsync(string key, CancellationToken ct)
    {
        if (!CanUseReports) return null;
        if (key == "dashboard") return IsAdmin ? new AnalyticsReadScope(IsAdmin: true) : null;
        if (ReportAccessService.RolesFor(key).Count == 0) return null;
        if (IsAdmin) return new(IsAdmin: true);
        var roles = await access.ReaderRolesAsync(key, User, ct);
        if (roles.Count == 0) return null;
        if (key is not ("evaluations" or "governance")) return new(IsAdmin: false);
        var isOfficer = roles.Contains(SystemRoles.SourcingOfficer) && await workflow.IsOfficer(await workflow.Actor(User));
        return new(false, isOfficer, ActorId, roles.Contains(SystemRoles.SourcingManager), roles.Contains(SystemRoles.TopCommittee));
    }

    [HttpGet, Authorize(Roles = SystemRoles.Admin)]
    public async Task<IActionResult> Access(CancellationToken ct)
    {
        if (!CanUseReports) return Forbid();
        var overrides = await access.ReadOverridesAsync(ct);
        return View(ReportingService.Catalog.SelectMany(report => ReportAccessService.RolesFor(report.Key).Select(role =>
            new AnalyticsAccessRow(report.Key, report.Title, role, overrides.GetValueOrDefault((report.Key, role), true)))).ToList());
    }

    [HttpPost, Authorize(Roles = SystemRoles.Admin)]
    public async Task<IActionResult> SetAccess(string key, string role, bool? allowed, CancellationToken ct)
    {
        if (!CanUseReports) return Forbid();
        if (!ModelState.IsValid || key == null || role == null || !allowed.HasValue || !ReportAccessService.RolesFor(key).Contains(role))
            return BadRequest();
        var entry = await db.ReportRoleAccess.FindAsync([key, role], ct);
        var previous = entry?.Allowed ?? true;
        if (previous != allowed.Value)
        {
            if (entry == null) db.ReportRoleAccess.Add(new ReportRoleAccess { ReportKey = key, Role = role, Allowed = allowed.Value });
            else entry.Allowed = allowed.Value;
            db.AuditLogs.Add(new AuditLog
            {
                UserId = ActorId, Action = "ReportAccessChanged", EntityName = nameof(ReportRoleAccess), EntityId = key,
                OldValues = JsonSerializer.Serialize(new { role, Allowed = previous }), NewValues = JsonSerializer.Serialize(new { role, Allowed = allowed }),
                CreatedAt = audit.UtcNow
            });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Conflict("Report access changed. Refresh and try again."); }
            catch (DbUpdateException) when (entry == null)
            {
                db.ChangeTracker.Clear();
                if (!await db.ReportRoleAccess.AnyAsync(r => r.ReportKey == key && r.Role == role, ct)) throw;
                return Conflict("Report access changed. Refresh and try again.");
            }
        }
        TempData["AnalyticsMessage"] = "Report access saved.";
        return RedirectToAction(nameof(Access));
    }

    [HttpGet, Authorize(Roles = SystemRoles.Admin)]
    public async Task<IActionResult> AuditEntry(long id, CancellationToken ct)
    {
        if (!CanUseReports) return Forbid();
        var entry = await db.AuditLogs.AsNoTracking().Include(a => a.User).SingleOrDefaultAsync(a => a.Id == id, ct);
        return entry == null ? NotFound() : View(entry);
    }

    [HttpGet, Authorize(Roles = SystemRoles.Admin)]
    public async Task<IActionResult> EvaluationPdf(int id, CancellationToken ct)
    {
        if (!CanUseReports) return Forbid();
        var available = await db.TenderReports.AnyAsync(r => r.Id == id && r.ReportType == ReportType.FullEvaluation && r.SubmittedAt != null && r.PdfContent != null, ct);
        if (!available) return NotFound();
        ProtectedReportCopy copy;
        try { copy = await workflow.AdminEvaluationPdf(id, User); }
        catch (WorkflowException exception) { return Conflict(exception.Message); }
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Canonical-SHA256"] = copy.CanonicalHash;
        Response.Headers["X-Access-Copy-SHA256"] = copy.CopyHash;
        return File(copy.Bytes, "application/pdf", copy.Name);
    }

    [HttpGet, Authorize(Roles = SystemRoles.Admin)]
    public async Task<IActionResult> Contracts(CancellationToken ct)
    {
        if (!CanUseReports) return Forbid();
        return View(await contracts.TenderOptionsAsync(ct));
    }

    [HttpPost, Authorize(Roles = SystemRoles.Admin), RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> AddContract(int tenderId, IFormFile? pdf, CancellationToken ct)
    {
        if (!CanUseReports) return Forbid();
        if (!ModelState.IsValid || pdf == null || !string.Equals(Path.GetExtension(pdf.FileName), ".pdf", StringComparison.OrdinalIgnoreCase) || !SupplierDocumentStorage.IsAllowed(pdf))
        {
            TempData["AnalyticsError"] = "Select a PDF up to 10 MB.";
            return RedirectToAction(nameof(Contracts));
        }
        if (!await contracts.SaveAsync(tenderId, pdf, ActorId, ct)) return NotFound();
        TempData["AnalyticsMessage"] = "Final contract saved.";
        return RedirectToAction(nameof(Governance), new { search = $"T-{tenderId:0000}", section = "contracts" });
    }

    [HttpGet, Authorize(Roles = SystemRoles.Admin)]
    public async Task<IActionResult> ContractPdf(int id, CancellationToken ct)
    {
        if (!CanUseReports) return Forbid();
        (byte[] Bytes, string Name)? contract;
        try { contract = await contracts.DownloadAsync(id, ActorId, ct); }
        catch (WorkflowException exception) { return Conflict(exception.Message); }
        if (contract is not { } document) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(document.Bytes, "application/pdf", document.Name);
    }
}
