using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.Supplier.Controllers;

[Area("Supplier"), Authorize(Roles = SystemRoles.Supplier)]
public sealed class NotificationsController(SupplierCommunicationService communication) : Controller
{
    public async Task<IActionResult> Index() => View(await communication.GetNotificationsAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        if (!await communication.MarkReadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id)) return NotFound();
        return RedirectToAction("Index");
    }
}
