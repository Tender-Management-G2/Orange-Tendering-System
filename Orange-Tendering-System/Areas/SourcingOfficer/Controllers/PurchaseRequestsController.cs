using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
//[Authorize(Roles = SystemRoles.SourcingOfficer)]
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
        request.Reviews.Add(new PurchaseRequestReview { PurchaseRequestId = id, SubmissionNumber = request.Reviews.Select(x => x.SubmissionNumber).DefaultIfEmpty(0).Max() + 1, SubmittedAt = DateTime.UtcNow, SubmittedSnapshotJson = submittedSnapshot, Decision = decision, Comment = comment?.Trim(), ReviewedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value, ReviewedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
