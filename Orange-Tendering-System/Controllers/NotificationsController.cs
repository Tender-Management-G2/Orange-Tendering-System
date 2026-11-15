// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Notifications
// Purpose: Notifications page plus the JSON API behind the live notification bell (feed, mark read, mark all read).
// Every query is scoped to the signed-in user's own Notification rows; links are same-site relative paths only.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    [AutoValidateAntiforgeryToken]
    public class NotificationsController(TenderWorkflowService workflow, ApplicationDbContext db) : Controller
    {
        public async Task<IActionResult> Index() => View(await workflow.Notifications(User));
        [HttpPost] public async Task<IActionResult> MarkRead() { await workflow.ReadNotifications(User); return RedirectToAction(nameof(Index)); }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // GET /Notifications/Feed: unread count and the newest notifications for the bell dropdown.
        [HttpGet]
        public async Task<IActionResult> Feed(int take = 12)
        {
            take = Math.Clamp(take, 1, 50);
            var unread = await db.Notifications.AsNoTracking().CountAsync(n => n.UserId == UserId && !n.IsRead);
            var items = await db.Notifications.AsNoTracking().Where(n => n.UserId == UserId)
                .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(take).ToListAsync();
            return Json(new { unread, items = items.Select(NotificationDto.From) });
        }

        [HttpPost]
        public async Task<IActionResult> Read(int id)
        {
            var changed = await db.Notifications.Where(n => n.Id == id && n.UserId == UserId).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
            if (changed == 0) return NotFound();
            return Json(new { unread = await db.Notifications.CountAsync(n => n.UserId == UserId && !n.IsRead) });
        }

        [HttpPost]
        public async Task<IActionResult> ReadAll()
        {
            await db.Notifications.Where(n => n.UserId == UserId && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
            return Json(new { unread = 0 });
        }
    }
}
