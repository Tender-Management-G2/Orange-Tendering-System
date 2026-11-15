using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

public sealed class PurchaseRequestService(ApplicationDbContext db, UserManager<ApplicationUser> users, TenderWorkflowService workflow)
{
    private async Task<ApplicationUser> Actor(ClaimsPrincipal principal, bool officer)
    {
        var user = await workflow.Actor(principal);
        TenderWorkflowService.Require(officer ? await workflow.IsOfficer(user) : await users.IsInRoleAsync(user, SystemRoles.RequestingDepartment) && user.DepartmentId.HasValue,
            "The assigned department account or single active Sourcing Officer is required.");
        return user;
    }
    public async Task<List<PurchaseRequest>> List(ClaimsPrincipal principal, bool officer)
    {
        var user = await Actor(principal, officer);
        return await db.PurchaseRequests.AsNoTracking().Where(p => officer || p.CreatedByUserId == user.Id).OrderByDescending(p => p.Id).ToListAsync();
    }
    public async Task<PurchaseRequest> Read(int id, ClaimsPrincipal principal, bool officer)
    {
        var user = await Actor(principal, officer);
        return await db.PurchaseRequests.Include(p => p.Items).Include(p => p.Reviews).SingleOrDefaultAsync(p => p.Id == id && (officer || p.CreatedByUserId == user.Id))
            ?? throw new WorkflowException("Purchase request not found or access denied.");
    }
    public async Task<int> Submit(string title, string justification, string itemName, decimal quantity, string unit, string specifications, ClaimsPrincipal principal)
    {
        var user = await Actor(principal, false);
        TenderWorkflowService.Require(!string.IsNullOrWhiteSpace(title) && title.Length <= 200 && !string.IsNullOrWhiteSpace(justification)
            && !string.IsNullOrWhiteSpace(itemName) && itemName.Length <= 200 && quantity > 0 && quantity <= 999999999999999.999m && decimal.Round(quantity, 3) == quantity && !string.IsNullOrWhiteSpace(unit) && unit.Length <= 30,
            "Supply a title, justification and a valid item quantity/unit.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = new PurchaseRequest { DepartmentId = user.DepartmentId!.Value, CreatedByUserId = user.Id, Title = title.Trim(), Justification = justification.Trim(),
            Status = PurchaseRequestStatus.Submitted, CreatedAt = workflow.Now, SubmittedAt = workflow.Now,
            Items = [new PurchaseRequestItem { Name = itemName.Trim(), Quantity = quantity, Unit = unit.Trim(), Specifications = specifications }] };
        request.Reviews.Add(new PurchaseRequestReview { SubmissionNumber = 1, SubmittedAt = workflow.Now,
            SubmittedSnapshotJson = JsonSerializer.Serialize(new { title, justification, itemName, quantity, unit, specifications }) });
        db.PurchaseRequests.Add(request); await db.SaveChangesAsync();
        db.AuditLogs.Add(new AuditLog { UserId = user.Id, EntityName = nameof(PurchaseRequest), EntityId = request.Id.ToString(), Action = "PurchaseRequestSubmitted", CreatedAt = workflow.Now });
        foreach (var officer in (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Where(u => u.IsActive))
            db.Notifications.Add(new Notification { UserId = officer.Id, Title = "Purchase request submitted", Message = request.Title, Link = $"/SourcingOfficer/PurchaseRequests/Review/{request.Id}", CreatedAt = workflow.Now });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return request.Id;
    }
    public async Task Decide(int id, ReviewDecision decision, string comment, ClaimsPrincipal principal)
    {
        var user = await Actor(principal, true);
        TenderWorkflowService.Require(decision is ReviewDecision.Accepted or ReviewDecision.Rejected or ReviewDecision.Returned && !string.IsNullOrWhiteSpace(comment) && comment.Length <= 2000, "A valid decision and feedback are required.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await db.PurchaseRequests.FromSqlInterpolated($"SELECT * FROM PurchaseRequests WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").Include(p => p.Reviews).SingleAsync();
        TenderWorkflowService.Require(request.Status == PurchaseRequestStatus.Submitted, "This submission has already been reviewed.");
        var review = request.Reviews.OrderByDescending(r => r.SubmissionNumber).First();
        review.Decision = decision; review.Comment = comment; review.ReviewedByUserId = user.Id; review.ReviewedAt = workflow.Now;
        request.Status = decision == ReviewDecision.Accepted ? PurchaseRequestStatus.Accepted : decision == ReviewDecision.Rejected ? PurchaseRequestStatus.Rejected : PurchaseRequestStatus.Returned;
        db.AuditLogs.Add(new AuditLog { UserId = user.Id, EntityName = nameof(PurchaseRequest), EntityId = id.ToString(), Action = "PurchaseRequestReviewed", NewValues = JsonSerializer.Serialize(new { decision, comment }), CreatedAt = workflow.Now });
        db.Notifications.Add(new Notification { UserId = request.CreatedByUserId, Title = "Purchase request decision", Message = decision + ": " + comment, Link = $"/Department/PurchaseRequests/Details/{id}", CreatedAt = workflow.Now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
