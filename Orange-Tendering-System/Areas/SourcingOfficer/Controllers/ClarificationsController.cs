using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using System.Security.Claims;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

// Backward-compatible links now lead to the one shared clarification inbox.
[Area("SourcingOfficer"), Authorize]
public class ClarificationsController(ApplicationDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public IActionResult Index() => RedirectToAction("Index", "Clarifications", new { area = "" });

    public IActionResult Details(int id) => RedirectToAction("Details", "Clarifications", new { area = "", id });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 10000) return BadRequest();
        var thread = await db.Clarifications.SingleOrDefaultAsync(c => c.Id == id
            && (c.FromUserId == UserId || c.ToUserId == UserId)
            && c.BidId != null && c.Bid!.TenderInvitation.InvitedByUserId == UserId
            && c.Status != ClarificationStatus.Closed);
        if (thread == null) return NotFound();

        var now = DateTime.UtcNow;
        db.ClarificationMessages.Add(new ClarificationMessage
        { ClarificationId = id, SenderUserId = UserId, Message = message.Trim(), SentAt = now });
        thread.Status = ClarificationStatus.Answered;
        db.Notifications.Add(new Notification
        {
            UserId = thread.FromUserId == UserId ? thread.ToUserId : thread.FromUserId,
            Title = "Clarification reply", Message = $"A reply was sent in clarification #{id}.",
            Link = $"/Supplier/Clarifications/Details/{id}", CreatedAt = now
        });
        await db.SaveChangesAsync();
        return RedirectToAction("Details", "Clarifications", new { area = "", id });
    }
}
