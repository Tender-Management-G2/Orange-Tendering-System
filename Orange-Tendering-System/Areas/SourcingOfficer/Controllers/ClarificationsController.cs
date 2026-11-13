using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using System.Security.Claims;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
public class ClarificationsController(ApplicationDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private IQueryable<Clarification> OfficerThreads() => db.Clarifications.Where(c => (c.FromUserId == UserId || c.ToUserId == UserId) && c.BidId != null && c.Bid!.TenderInvitation.InvitedByUserId == UserId);

    public async Task<IActionResult> Index() => View(await OfficerThreads().AsNoTracking().Include(c => c.Bid).ThenInclude(b => b!.TenderInvitation).ThenInclude(i => i.Supplier).OrderByDescending(c => c.CreatedAt).ToListAsync());

    public async Task<IActionResult> Details(int id)
    {
        var thread = await OfficerThreads().AsNoTracking().Include(c => c.Bid).ThenInclude(b => b!.TenderInvitation).ThenInclude(i => i.Supplier).Include(c => c.Messages).ThenInclude(m => m.SenderUser).SingleOrDefaultAsync(c => c.Id == id);
        return thread == null ? NotFound() : View(thread);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 10000) return BadRequest();
        var thread = await OfficerThreads().SingleOrDefaultAsync(c => c.Id == id && c.Status != ClarificationStatus.Closed);
        if (thread == null) return NotFound();
        db.ClarificationMessages.Add(new ClarificationMessage { ClarificationId = id, SenderUserId = UserId, Message = message.Trim(), SentAt = DateTime.UtcNow });
        thread.Status = ClarificationStatus.Answered;
        db.Notifications.Add(new Notification { UserId = thread.FromUserId == UserId ? thread.ToUserId : thread.FromUserId, Title = "Clarification reply", Message = $"A reply was sent in clarification #{id}.", Link = $"/Supplier/Clarifications/Details/{id}", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); return RedirectToAction(nameof(Details), new { id });
    }
}
