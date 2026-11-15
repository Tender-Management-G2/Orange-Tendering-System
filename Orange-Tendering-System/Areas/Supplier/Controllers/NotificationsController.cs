using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.Supplier.Controllers;

[Area("Supplier"), Authorize(Roles = SystemRoles.Supplier)]
public sealed class NotificationsController(SupplierCommunicationService communication) : Controller
{
    public async Task<IActionResult> Index(string? search, string? status)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        ViewData["Search"] = search?.Trim();
        ViewData["Status"] = status;
        return View(await communication.GetNotificationsAsync(userId, search, status));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        if (!await communication.MarkReadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id)) return NotFound();
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        await communication.MarkAllReadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        TempData["NotificationMessage"] = "All notifications are marked as read.";
        return RedirectToAction(nameof(Index));
    }
}
