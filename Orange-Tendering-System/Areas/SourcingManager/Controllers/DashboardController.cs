
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;
using System.Security.Claims;

namespace Orange_Tendering_System.Areas.SourcingManager.Controllers
{
    [Area("SourcingManager")]
    [Authorize(Roles = SystemRoles.SourcingManager)]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Sourcing Manager Dashboard
        // Owner: Haya
        // Trello: https://trello.com/c/ZkMN8s14

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Challenge();
            }

            // Count FullEvaluation reports by status
            var reportCounts = await _context.TenderReports
                .Where(r => r.ReportType == ReportType.FullEvaluation)
                .GroupBy(r => r.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .AsNoTracking()
                .ToListAsync();

            int CountReports(ReportStatus status)
            {
                return reportCounts
                    .Where(r => r.Status == status)
                    .Sum(r => r.Count);
            }

            var pending = CountReports(ReportStatus.Submitted);
            var approved = CountReports(ReportStatus.Accepted);
            var rejected = CountReports(ReportStatus.Rejected);
            var returned = CountReports(ReportStatus.Returned);


            // Reports waiting for manager review
            var pendingReports = await _context.TenderReports
                .Include(r => r.TenderRound)
                    .ThenInclude(tr => tr.Tender)
                .Where(r =>
                    r.ReportType == ReportType.FullEvaluation &&
                    r.Status == ReportStatus.Submitted)
                .OrderByDescending(r => r.SubmittedAt)
                .Take(5)
                .AsNoTracking()
                .ToListAsync();

            // Accepted reports that have not been forwarded yet
            var acceptedReports = await _context.TenderReports
                .Include(r => r.TenderRound)
                    .ThenInclude(tr => tr.Tender)
                .Where(r =>
                    r.ReportType == ReportType.FullEvaluation &&
                    r.Status == ReportStatus.Accepted &&
                    !_context.TenderReports.Any(s =>
                        s.ReportType == ReportType.ExecutiveSummary &&
                        s.PreviousReportId == r.Id &&
                        s.Status == ReportStatus.Submitted))
                .OrderByDescending(r => r.ReviewedAt)
                .Take(5)
                .AsNoTracking()
                .ToListAsync();

            // Decisions made by the current manager
            var recentDecisions = await _context.TenderReports
                .Include(r => r.TenderRound)
                    .ThenInclude(tr => tr.Tender)
                .Where(r =>
                    r.ReportType == ReportType.FullEvaluation &&
                    r.ReviewedByUserId == userId &&
                    r.ReviewedAt != null)
                .OrderByDescending(r => r.ReviewedAt)
                .Take(5)
                .AsNoTracking()
                .ToListAsync();

            var tasks = pendingReports
                .Select(r => new DashboardTask(
                    $"Review Report #{r.Id}",
                    r.TenderRound.Tender.Title,
                    "Pending Review"))
                .Concat(acceptedReports.Select(r => new DashboardTask(
                    $"Forward Report #{r.Id}",
                    r.TenderRound.Tender.Title,
                    "Ready to Forward")))
                .ToList();

            var activities = recentDecisions
                .Select(r => new DashboardActivity(
                    $"Report #{r.Id} - {r.Status}",
                    r.TenderRound.Tender.Title,
                    r.ReviewedAt?.ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm") ?? "—"
                ))
                .ToList();

            var model = new WorkspaceDashboardViewModel
            {
                DataConnected = true,

                DisplayName = User.Identity?.Name
                    ?? "Sourcing Manager",

                Metrics = new Dictionary<string, int>
                {
                    ["pending-reviews"] = pending,
                    ["approved-reports"] = approved,
                    ["rejected-reports"] = rejected,
                    ["returned-reports"] = returned,
                    ["reports-total"] = reportCounts.Sum(r => r.Count)
                },

                Tasks = tasks,

                Activities = activities,

                Statuses = new List<DashboardStatus>
                {
                    new("Pending Review", pending, "#ff8a00"),
                    new("Approved", approved, "#00bf81"),
                    new("Rejected", rejected, "#ef4444"),
                    new("Returned", returned, "#098fff")
                }
            };

            return View(model);
        }
    }
}
