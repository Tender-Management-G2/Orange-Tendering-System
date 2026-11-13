// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: ALL TEAM
// Module: Clarifications, Notifications & Audit Trail
// Purpose: MVC endpoints for the Notifications pages of the shared signed-in pages.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using System.Security.Claims;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public NotificationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Notifications — Owner: ALL TEAM · Trello: Clarifications, Notifications & Audit Trail (https://trello.com/c/N1OFBdNM)
        // GET /Notifications. TODO: query the data in a service and pass a view model.
        public async Task<IActionResult> Index(string? search, string? status)
        {
            // This implementation is intentionally scoped to the Sourcing Manager
            // workspace. Other shared workspaces retain their existing scaffold.
            if (!User.IsInRole(SystemRoles.SourcingManager))
            {
                return View();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var notificationQuery = _context.Notifications
                .Where(notification => notification.UserId == userId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                notificationQuery = notificationQuery.Where(notification =>
                    notification.Title.Contains(term) ||
                    notification.Message.Contains(term));
            }

            if (string.Equals(status, "Unread", StringComparison.OrdinalIgnoreCase))
            {
                notificationQuery = notificationQuery.Where(notification => !notification.IsRead);
            }
            else if (string.Equals(status, "Read", StringComparison.OrdinalIgnoreCase))
            {
                notificationQuery = notificationQuery.Where(notification => notification.IsRead);
            }

            var notifications = await notificationQuery
                .OrderBy(notification => notification.IsRead)
                .ThenByDescending(notification => notification.CreatedAt)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.Status = status;
            return View(notifications);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            if (!User.IsInRole(SystemRoles.SourcingManager))
            {
                return Forbid();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var updated = await _context.Notifications
                .Where(notification => notification.UserId == userId && !notification.IsRead)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(notification => notification.IsRead, true));

            TempData["Success"] = updated == 0
                ? "There are no unread notifications."
                : "All notifications were marked as read.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Open(int id)
        {
            if (!User.IsInRole(SystemRoles.SourcingManager))
            {
                return Forbid();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var notification = await _context.Notifications
                .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);

            if (notification == null)
            {
                return NotFound();
            }

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            if (string.IsNullOrWhiteSpace(notification.Link) ||
                !notification.Link.StartsWith('/'))
            {
                return RedirectToAction(nameof(Index));
            }

            return LocalRedirect(notification.Link);
        }
    }
}
