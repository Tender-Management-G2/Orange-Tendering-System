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
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return Forbid();

        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == user.Id)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(100).ToListAsync(cancellationToken);
        ViewData["UnreadCount"] = notifications.Count(n => !n.IsRead);
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
}
