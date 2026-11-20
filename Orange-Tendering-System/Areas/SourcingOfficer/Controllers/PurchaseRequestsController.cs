using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
public class PurchaseRequestsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.PurchaseRequests.AsNoTracking()
        .Include(r => r.Department).Include(r => r.Tenders).OrderByDescending(r => r.SubmittedAt ?? r.CreatedAt).ToListAsync());

    public async Task<IActionResult> Review(int id)
    {
        var request = await db.PurchaseRequests.AsNoTracking().Include(r => r.Department).Include(r => r.CreatedByUser).Include(r => r.Items).ThenInclude(i => i.Category)
            .Include(r => r.Documents).Include(r => r.Reviews).Include(r => r.Tenders).AsSplitQuery().SingleOrDefaultAsync(r => r.Id == id);
        return request == null ? NotFound() : View(request);
    }

    // Department supporting files of a request, for the Sourcing Officer's review (private storage, never wwwroot).
    public async Task<IActionResult> Download(int id, [FromServices] IWebHostEnvironment environment)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.PurchaseRequestId != null && d.DocumentType == "DepartmentSupportingDocument");
        if (document == null || Path.GetFileName(document.StoragePath) != document.StoragePath) return NotFound();
        var path = Path.Combine(environment.ContentRootPath, "Areas", "Department", "App_Data", "Uploads", document.StoragePath);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "application/octet-stream", document.FileName);
    }

    // Accepting leads straight to tender preparation for this request; a request that already has its tender opens that tender.
    // Used by the acceptance redirect, the Officer's task notification and the request list, so preparation can be resumed later.
    [HttpGet]
    public async Task<IActionResult> Prepare(int id)
    {
        var request = await db.PurchaseRequests.AsNoTracking().Where(r => r.Id == id).Select(r => new { r.Status, Tender = r.Tenders.Select(t => (int?)t.Id).FirstOrDefault() }).SingleOrDefaultAsync();
        if (request == null) return NotFound();
        if (request.Tender is int tenderId) return RedirectToAction("Index", "Workflow", new { open = tenderId });
        if (request.Status != PurchaseRequestStatus.Accepted) return RedirectToAction(nameof(Review), new { id });
        return RedirectToAction("Index", "Workflow", new { request = id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, ReviewDecision decision, string? comment)
    {
        if (decision is not (ReviewDecision.Accepted or ReviewDecision.Returned or ReviewDecision.Rejected)) return BadRequest();
        if (decision is ReviewDecision.Returned or ReviewDecision.Rejected && string.IsNullOrWhiteSpace(comment))
            return BadRequest("Record the reason or the additional documents required before returning or rejecting the request.");
        if (comment?.Trim().Length > 2000) return BadRequest("The comment must be 2000 characters or fewer.");
        var reviewerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
        // Row lock: a double click, a refresh or a second Officer tab cannot record two decisions for one submission.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var locked = await db.PurchaseRequests.FromSqlInterpolated($"SELECT * FROM PurchaseRequests WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").AnyAsync();
        if (!locked) return NotFound();
        var request = await db.PurchaseRequests.Include(r => r.Reviews).Include(r => r.Items).Include(r => r.Tenders).SingleAsync(r => r.Id == id);
        if (request.Status != PurchaseRequestStatus.Submitted)
        {
            // Retrying a completed acceptance is harmless: continue where the first one led.
            if (decision == ReviewDecision.Accepted && request.Status == PurchaseRequestStatus.Accepted) return RedirectToAction(nameof(Prepare), new { id });
            TempData.Orange(OrangeFeedback.Popup("Already reviewed", $"PR-{id} was already reviewed ({request.Status}).", OrangeVariant.Return));
            return RedirectToAction(nameof(Index));
        }
        request.Status = decision switch { ReviewDecision.Accepted => PurchaseRequestStatus.Accepted, ReviewDecision.Returned => PurchaseRequestStatus.Returned, _ => PurchaseRequestStatus.Rejected };
        var submittedSnapshot = System.Text.Json.JsonSerializer.Serialize(new { request.Title, request.Justification, request.Priority, request.NeededBy, Items = request.Items.Select(i => new { i.Name, i.CategoryId, i.Specifications, i.Quantity, i.Unit }) });
        var now = DateTime.UtcNow;
        // The Department's submission already opened a Pending review row: complete it instead of adding a second, orphan row.
        var pending = request.Reviews.Where(x => x.Decision == ReviewDecision.Pending).OrderByDescending(x => x.SubmissionNumber).FirstOrDefault();
        if (pending != null) { pending.Decision = decision; pending.Comment = comment?.Trim(); pending.ReviewedByUserId = reviewerId; pending.ReviewedAt = now; }
        else request.Reviews.Add(new PurchaseRequestReview { PurchaseRequestId = id, SubmissionNumber = request.Reviews.Select(x => x.SubmissionNumber).DefaultIfEmpty(0).Max() + 1, SubmittedAt = now, SubmittedSnapshotJson = submittedSnapshot, Decision = decision, Comment = comment?.Trim(), ReviewedByUserId = reviewerId, ReviewedAt = now });
        // Tell the requesting department what happened (same notification the workflow service sends), linked to the request.
        db.Notifications.Add(new Notification { UserId = request.CreatedByUserId, Title = "Purchase request decision", Message = $"{decision}: {comment?.Trim()}", Link = $"/Department/PurchaseRequests/Details/{id}", CreatedAt = now });
        // The Officer's own task: resume tender preparation from the bell or the dashboard at any time.
        if (decision == ReviewDecision.Accepted)
            db.Notifications.Add(new Notification { UserId = reviewerId, Title = "Prepare the tender", Message = $"PR-{id} ({request.Title}) is accepted. Prepare its tender: requirements, documents and the evaluation committee.", Link = $"/SourcingOfficer/PurchaseRequests/Prepare/{id}", CreatedAt = now });
        db.AuditLogs.Add(new AuditLog { UserId = reviewerId, EntityName = "PurchaseRequest", EntityId = id.ToString(), Action = "PurchaseRequestReview" + decision, CreatedAt = now,
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { Decision = decision.ToString(), Comment = comment?.Trim(), ReviewedAt = now }) });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData.Orange(decision switch
        {
            ReviewDecision.Accepted => OrangeFeedback.Popup("Request accepted!", $"PR-{id} is accepted. Its items and details are already filled in: complete the tender preparation.", OrangeVariant.Approve),
            ReviewDecision.Returned => OrangeFeedback.Popup("Request returned for correction", $"PR-{id} went back to the department with your comments.", OrangeVariant.Return),
            _ => OrangeFeedback.Popup("Request rejected", $"The rejection of PR-{id} and its reason are recorded.", OrangeVariant.Return)
        });
        return decision == ReviewDecision.Accepted ? RedirectToAction(nameof(Prepare), new { id }) : RedirectToAction(nameof(Index));
    }
}
