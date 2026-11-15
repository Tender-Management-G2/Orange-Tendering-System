using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Controllers;

[Authorize]
public sealed class NotificationsController(ApplicationDbContext db, UserManager<ApplicationUser> users) : Controller
{
    public async Task<IActionResult> Index(string? search, string? status, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return Forbid();

        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == user.Id);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(n => n.Title.Contains(term) || n.Message.Contains(term));
        }
        if (string.Equals(status, "Unread", StringComparison.OrdinalIgnoreCase))
            query = query.Where(n => !n.IsRead);
        else if (string.Equals(status, "Read", StringComparison.OrdinalIgnoreCase))
            query = query.Where(n => n.IsRead);

        var notifications = await query
            .OrderBy(n => n.IsRead)
            .ThenByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(100).ToListAsync(cancellationToken);
        ViewData["UnreadCount"] = notifications.Count(n => !n.IsRead);
        ViewData["Search"] = search;
        ViewData["Status"] = status;
        return View(notifications);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return Forbid();

        var notification = await db.Notifications
            .SingleOrDefaultAsync(n => n.Id == id && n.UserId == user.Id, cancellationToken);
        if (notification is not null && !notification.IsRead)
        {
            notification.IsRead = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return Forbid();

        await db.Notifications
            .Where(n => n.UserId == user.Id && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Open(int id, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return Forbid();

        var notification = await db.Notifications
            .SingleOrDefaultAsync(n => n.Id == id && n.UserId == user.Id, cancellationToken);
        if (notification is null) return NotFound();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        return !string.IsNullOrWhiteSpace(notification.Link) && Url.IsLocalUrl(notification.Link)
            ? LocalRedirect(notification.Link)
            : RedirectToAction(nameof(Index));
    }
}
