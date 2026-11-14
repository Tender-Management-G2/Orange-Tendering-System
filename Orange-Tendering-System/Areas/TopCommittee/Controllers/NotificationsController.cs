using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = SystemRoles.TopCommittee)]
public sealed class NotificationsController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    : TopCommitteeControllerBase(db, users)
{
    [HttpGet]
    public async Task<IActionResult> Preview(CancellationToken cancellationToken)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();

        Response.Headers.CacheControl = "no-store";
        var unreadCount = await Db.Notifications.CountAsync(n => n.UserId == user.Id && !n.IsRead, cancellationToken);
        var recent = await Db.Notifications.AsNoTracking().Where(n => n.UserId == user.Id)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(5)
            .Select(n => new { n.Id, n.Title, n.Message, n.Link, n.IsRead, n.CreatedAt }).ToListAsync(cancellationToken);
        var items = new List<object>(recent.Count);
        foreach (var item in recent)
        {
            var destination = await AuthorizedDestination(user.Id, item.Link, item.Title, item.CreatedAt, cancellationToken);
            items.Add(new
            {
                item.Id,
                item.Title,
                item.Message,
                item.IsRead,
                CreatedAt = item.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                CanOpen = destination is not null
            });
        }
        return Json(new { unreadCount, items });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(int id, CancellationToken cancellationToken)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();

        var notification = await Db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == user.Id, cancellationToken);
        if (notification is null) return NotFound();

        var destination = await AuthorizedDestination(user.Id, notification.Link, notification.Title, notification.CreatedAt, cancellationToken);
        if (destination is null)
            return Forbid();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await Db.SaveChangesAsync(cancellationToken);
        }

        Response.Headers.CacheControl = "no-store";
        var unreadCount = await Db.Notifications.CountAsync(n => n.UserId == user.Id && !n.IsRead, cancellationToken);
        return Json(new { url = destination, unreadCount });
    }

    private async Task<string?> AuthorizedDestination(string userId, string? link, string? title, DateTime createdAt, CancellationToken cancellationToken)
    {
        // Older Manager notifications have no deep link. Resolve only a nearby,
        // submitted Executive Summary from a round assigned to this member.
        if (string.IsNullOrWhiteSpace(link) && title == "New Executive Summary")
        {
            var candidates = await Committee.AssignedRounds(userId)
                .Where(r => r.Status == CommitteeRoundStatus.InProgress &&
                    r.TenderReport != null && r.TenderReport.ReportType == ReportType.ExecutiveSummary &&
                    r.TenderReport.Status == ReportStatus.Submitted && r.TenderReport.SubmittedAt != null)
                .Select(r => new { r.Id, SubmittedAt = r.TenderReport!.SubmittedAt })
                .ToListAsync(cancellationToken);
            var matches = candidates
                .Select(candidate => new
                {
                    candidate.Id,
                    Difference = Math.Abs((candidate.SubmittedAt!.Value - createdAt).TotalSeconds)
                })
                .Where(candidate => candidate.Difference <= 120)
                .OrderBy(candidate => candidate.Difference)
                .ToArray();
            if (matches.Length > 0 && (matches.Length == 1 || matches[0].Difference < matches[1].Difference))
                return Url.Action("Decision", "Decisions", new { area = "TopCommittee", id = matches[0].Id });
        }

        // Shared workflow notifications identify the tender, while Top pages
        // identify the committee round. Resolve only within this user's assignments.
        const string workflowPrefix = "/Workflow/Details/";
        if (link?.StartsWith(workflowPrefix, StringComparison.Ordinal) == true &&
            int.TryParse(link.AsSpan(workflowPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var tenderId) && tenderId > 0)
        {
            var assignedRound = await Committee.AssignedRounds(userId)
                .Where(r => r.TenderRound.TenderId == tenderId)
                .OrderByDescending(r => r.TenderRound.RoundNumber).ThenByDescending(r => r.RoundNumber)
                .Select(r => (int?)r.Id).FirstOrDefaultAsync(cancellationToken);
            if (assignedRound.HasValue)
                return Url.Action("Decision", "Decisions", new { area = "TopCommittee", id = assignedRound.Value });
        }
        var threadId = ThreadIdFor(link);
        if (threadId.HasValue && await new PrivateClarificationService(Db, Users).Read(userId, threadId.Value) is not null)
            return Url.Action("Details", "Conversations", new { area = "TopCommittee", id = threadId.Value });

        var roundId = DecisionRoundIdFor(link);
        if (roundId.HasValue && await new TopCommitteeService(Db, Users).AssignedRounds(userId)
                .AnyAsync(round => round.Id == roundId.Value, cancellationToken))
            return Url.Action("Decision", "Decisions", new { area = "TopCommittee", id = roundId.Value });

        return null;
    }

    private static int? ThreadIdFor(string? link)
    {
        const string prefix = "/TopCommittee/Conversations/Details/";
        if (link is null || !link.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(link.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return null;
        return id;
    }

    private static int? DecisionRoundIdFor(string? link)
    {
        const string prefix = "/TopCommittee/Decisions/Decision/";
        if (link is null || !link.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(link.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return null;
        return id;
    }
}
