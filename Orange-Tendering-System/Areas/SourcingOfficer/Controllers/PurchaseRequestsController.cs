using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
public class PurchaseRequestsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.PurchaseRequests.AsNoTracking()
        .Include(r => r.Department).Include(r => r.Tenders).OrderByDescending(r => r.SubmittedAt ?? r.CreatedAt).ToListAsync());

    public async Task<IActionResult> Review(int id)
    {
        var request = await db.PurchaseRequests.AsNoTracking().Include(r => r.Department).Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id);
        return request == null ? NotFound() : View(request);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, ReviewDecision decision, string? comment)
    {
        var request = await db.PurchaseRequests.Include(r => r.Reviews).SingleOrDefaultAsync(r => r.Id == id);
        if (request == null) return NotFound();
        if (request.Status != PurchaseRequestStatus.Submitted || decision == ReviewDecision.Pending) return BadRequest();
        request.Status = decision switch { ReviewDecision.Accepted => PurchaseRequestStatus.Accepted, ReviewDecision.Returned => PurchaseRequestStatus.Returned, _ => PurchaseRequestStatus.Rejected };
        var submittedSnapshot = System.Text.Json.JsonSerializer.Serialize(new { request.Title, request.Justification, request.Priority, request.NeededBy, Items = request.Items.Select(i => new { i.Name, i.Specifications, i.Quantity, i.Unit }) });
        var reviewerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
        var now = DateTime.UtcNow;
        // The Department's submission already opened a Pending review row: complete it instead of adding a second, orphan row.
        var pending = request.Reviews.Where(x => x.Decision == ReviewDecision.Pending).OrderByDescending(x => x.SubmissionNumber).FirstOrDefault();
        if (pending != null) { pending.Decision = decision; pending.Comment = comment?.Trim(); pending.ReviewedByUserId = reviewerId; pending.ReviewedAt = now; }
        else request.Reviews.Add(new PurchaseRequestReview { PurchaseRequestId = id, SubmissionNumber = request.Reviews.Select(x => x.SubmissionNumber).DefaultIfEmpty(0).Max() + 1, SubmittedAt = now, SubmittedSnapshotJson = submittedSnapshot, Decision = decision, Comment = comment?.Trim(), ReviewedByUserId = reviewerId, ReviewedAt = now });
        // Tell the requesting department what happened (same notification the workflow service sends), linked to the request.
        db.Notifications.Add(new Notification { UserId = request.CreatedByUserId, Title = "Purchase request decision", Message = $"{decision}: {comment?.Trim()}", Link = $"/Department/PurchaseRequests/Details/{id}", CreatedAt = now });
        db.AuditLogs.Add(new AuditLog { UserId = reviewerId, EntityName = "PurchaseRequest", EntityId = id.ToString(), Action = "PurchaseRequestReview" + decision, CreatedAt = now });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
