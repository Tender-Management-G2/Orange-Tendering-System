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
            var destination = await AuthorizedDestination(user.Id, item.Link, cancellationToken);
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

        var destination = await AuthorizedDestination(user.Id, notification.Link, cancellationToken);
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

    private async Task<string?> AuthorizedDestination(string userId, string? link, CancellationToken cancellationToken)
    {
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
