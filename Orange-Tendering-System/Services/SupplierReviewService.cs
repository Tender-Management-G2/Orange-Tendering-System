using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// Administrative review of suppliers created by the Identity OTP registration flow.
public class SupplierReviewService(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IWebHostEnvironment environment, TimeProvider clock)
{
    private async Task<ApplicationUser> Admin(ClaimsPrincipal principal)
    {
        var actor = await users.GetUserAsync(principal);
        TenderWorkflowService.Require(actor is { IsActive: true } && await users.IsInRoleAsync(actor, SystemRoles.Admin), "Active Admin authority is required to review supplier registrations.");
        return actor;
    }
    private IQueryable<Supplier> SubmittedSuppliers() => db.Suppliers.Where(s =>
        !db.Users.Any(u => u.SupplierId == s.Id) ||
        db.Users.Any(u => u.SupplierId == s.Id && u.EmailConfirmed));

    public async Task<List<Supplier>> List(ClaimsPrincipal principal) { await Admin(principal); return await SubmittedSuppliers().AsNoTracking().OrderByDescending(x => x.Id).ToListAsync(); }
    public async Task<Supplier> Read(int id, ClaimsPrincipal principal)
    { await Admin(principal); return await SubmittedSuppliers().Include(x => x.Documents).SingleOrDefaultAsync(x => x.Id == id) ?? throw new WorkflowException("Supplier not found."); }
    public async Task Decide(int id, bool approve, string notes, ClaimsPrincipal principal)
    {
        var actor = await Admin(principal);
        TenderWorkflowService.Require(!string.IsNullOrWhiteSpace(notes) && notes.Length <= 1000, "Record the document verification findings and decision reason (up to 1000 characters).");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var company = await db.Suppliers.FromSqlInterpolated($"SELECT * FROM Suppliers WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").Include(x => x.Documents).SingleOrDefaultAsync();
        TenderWorkflowService.Require(company?.Status == SupplierStatus.Pending, "This supplier is not awaiting a registration review.");
        TenderWorkflowService.Require(!await db.Users.AnyAsync(u => u.SupplierId == id) ||
            await db.Users.AnyAsync(u => u.SupplierId == id && u.EmailConfirmed),
            "The supplier must verify its contact email before review.");
        TenderWorkflowService.Require(!approve || company.Documents.Any(x => x.DocumentType == "CommercialRegistration" && File.Exists(PrivateDocumentPaths.Resolve(environment.ContentRootPath, x.StoragePath))), "A stored commercial registration document must be available before approval.");
        company.Status = approve ? SupplierStatus.Approved : SupplierStatus.Rejected; company.ReviewedByUserId = actor.Id; company.ReviewedAt = clock.GetUtcNow().UtcDateTime; company.ReviewNotes = notes;
        foreach (var doc in company.Documents) { doc.VerificationStatus = approve ? DocumentVerificationStatus.Verified : DocumentVerificationStatus.Rejected; doc.VerifiedByUserId = actor.Id; doc.VerifiedAt = company.ReviewedAt; doc.VerificationNotes = notes; }
        db.AuditLogs.Add(new AuditLog { UserId = actor.Id, EntityName = "Supplier", EntityId = id.ToString(), Action = approve ? "SupplierApproved" : "SupplierRejected", NewValues = System.Text.Json.JsonSerializer.Serialize(new { notes }), CreatedAt = clock.GetUtcNow().UtcDateTime });
        foreach (var userId in await db.Users.Where(x => x.SupplierId == id).Select(x => x.Id).ToListAsync()) db.Notifications.Add(new Notification { UserId = userId, Title = "Supplier registration decision", Message = company.Status + ": " + notes, Link = "/Supplier/Workflow", CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task<(string Path, string Name)> Document(int id, ClaimsPrincipal principal)
    {
        await Admin(principal); var doc = await db.Documents.SingleOrDefaultAsync(x => x.Id == id && x.SupplierId != null);
        TenderWorkflowService.Require(doc != null, "Supplier document not found.");
        var path = PrivateDocumentPaths.Resolve(environment.ContentRootPath, doc.StoragePath);
        TenderWorkflowService.Require(File.Exists(path), "Document is unavailable.");
        return (path, doc.FileName);
    }
}
