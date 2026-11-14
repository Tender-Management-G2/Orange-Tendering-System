using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingManager.Controllers;

[Area("SourcingManager")]
[Authorize(Roles = SystemRoles.SourcingManager)]
public sealed class AnalyticsController(ApplicationDbContext context) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? status)
    {
        var model = await BuildModel(search, status);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? search, string? status)
    {
        var model = await BuildModel(search, status);
        var csv = new StringBuilder();
        csv.AppendLine("Tender,Report Version,Submitted By,Submitted At,Manager Decision,Reviewed At,Top Committee Status,Top Committee Decided At");

        foreach (var row in model.Rows)
        {
            csv.AppendLine(string.Join(",",
                Csv(row.TenderTitle),
                row.VersionNumber,
                Csv(row.SubmittedBy),
                Csv(row.SubmittedAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty),
                row.Status,
                Csv(row.ReviewedAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty),
                row.TopCommitteeStatus?.ToString() ?? string.Empty,
                Csv(row.TopCommitteeDecidedAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty)));
        }

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv",
            $"sourcing-manager-report-analytics-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private async Task<SourcingManagerAnalyticsViewModel> BuildModel(string? search, string? status)
    {
        var reports = await context.TenderReports
            .AsNoTracking()
            .Include(report => report.TenderRound)
                .ThenInclude(round => round.Tender)
            .Include(report => report.CreatedByUser)
            .Where(report => report.ReportType == ReportType.FullEvaluation)
            .OrderByDescending(report => report.SubmittedAt)
            .ToListAsync();

        var reportIds = reports.Select(report => report.Id).ToList();
        var topRounds = await context.CommitteeRounds
            .AsNoTracking()
            .Where(round => round.CommitteeType == CommitteeType.Top &&
                            round.TenderReportId.HasValue &&
                            reportIds.Contains(round.TenderReportId.Value))
            .ToListAsync();
        var topRoundByReportId = topRounds
            .GroupBy(round => round.TenderReportId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(round => round.StartedAt).First());

        var allRows = reports.Select(report =>
        {
            topRoundByReportId.TryGetValue(report.Id, out var topRound);
            return new SourcingManagerAnalyticsRow(
                report.Id,
                report.TenderRound.Tender.Title,
                report.VersionNumber,
                report.CreatedByUser.Email ?? "—",
                report.SubmittedAt,
                report.Status,
                report.ReviewedAt,
                topRound?.Status,
                topRound?.CompletedAt);
        }).ToList();

        var filteredRows = allRows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filteredRows = filteredRows.Where(row =>
                row.TenderTitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.SubmittedBy.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (Enum.TryParse<ReportStatus>(status, true, out var selectedStatus))
            filteredRows = filteredRows.Where(row => row.Status == selectedStatus);

        return new SourcingManagerAnalyticsViewModel
        {
            SubmittedCount = allRows.Count(row => row.Status == ReportStatus.Submitted),
            AcceptedCount = allRows.Count(row => row.Status == ReportStatus.Accepted),
            ReturnedCount = allRows.Count(row => row.Status == ReportStatus.Returned),
            RejectedCount = allRows.Count(row => row.Status == ReportStatus.Rejected),
            AwaitingTopCommitteeCount = topRounds.Count(round => round.Status == CommitteeRoundStatus.InProgress),
            CompletedTopCommitteeCount = topRounds.Count(round => round.Status != CommitteeRoundStatus.InProgress),
            Search = search,
            Status = status,
            Rows = filteredRows.ToList()
        };
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
