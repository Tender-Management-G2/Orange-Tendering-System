using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.Department.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.Department.Services;

// Department-local service, explicitly constructed by the controllers so no shared DI setup is changed.
public sealed class DepartmentRequestService(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public IQueryable<PurchaseRequest> Requests(int departmentId) => db.PurchaseRequests
        .Where(r => r.DepartmentId == departmentId).Include(r => r.Tenders);

    public Task<PurchaseRequest?> Load(int departmentId, int id) => Requests(departmentId)
        .Include(r => r.Items).ThenInclude(i => i.Category).Include(r => r.Documents).Include(r => r.Reviews)
        .ThenInclude(r => r.ReviewedByUser).Include(r => r.Department).Include(r => r.CreatedByUser)
        .AsSplitQuery().SingleOrDefaultAsync(r => r.Id == id);

    private static RequestSnapshot Snapshot(PurchaseRequest request) => new(request.Title,
        request.Justification, request.Priority, request.NeededBy,
        request.Items.OrderBy(i => i.Id).Select(i => new RequestItemForm
        { Name = i.Name, CategoryId = i.CategoryId, CategoryName = i.Category?.Name, Specifications = i.Specifications, Quantity = i.Quantity, Unit = i.Unit }).ToList(),
        request.Documents.OrderBy(d => d.Id).Select(d => d.FileName).ToList(), request.EvaluationMethod);

    public static string Version(PurchaseRequest request) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            request.Id, request.Status, request.SubmittedAt, Content = Snapshot(request),
            Reviews = request.Reviews.OrderBy(r => r.Id).Select(r => new { r.Id, r.Decision, r.Comment, r.ReviewedAt }),
            Tenders = request.Tenders.OrderBy(t => t.Id).Select(t => t.Id)
        }))));

    public static RequestForm Form(PurchaseRequest request) => new()
    {
        Id = request.Id, Version = Version(request), Title = request.Title,
        Justification = request.Justification, Priority = request.Priority, EvaluationMethod = request.EvaluationMethod, NeededBy = request.NeededBy,
        Items = Snapshot(request).Items, ExistingDocuments = request.Documents.ToList(),
        ReturnComment = request.Status == PurchaseRequestStatus.Returned
            ? request.Reviews.OrderByDescending(r => r.SubmissionNumber).FirstOrDefault()?.Comment : null
    };

    // "Laptop + 2 more items — IT Department": meaningful, consistent and searchable; at most 200 characters.
    public static string GenerateTitle(IReadOnlyList<string> itemNames, string department)
    {
        var first = itemNames.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim() ?? "Request";
        var more = itemNames.Count - 1;
        var suffix = (more > 0 ? $" + {more} more item{(more == 1 ? "" : "s")}" : "") + " — " + department;
        var room = Math.Max(10, 200 - suffix.Length);
        if (first.Length > room) first = first[..(room - 1)].TrimEnd() + "…";
        return (first + suffix).Length <= 200 ? first + suffix : (first + suffix)[..200];
    }

    public async Task<int> Save(ApplicationUser user, RequestForm form, bool submit, string storageRoot)
    {
        if ((submit && form.EvaluationMethod == null) || (form.EvaluationMethod.HasValue && !Enum.IsDefined(form.EvaluationMethod.Value)))
            throw new InvalidOperationException("Choose a valid evaluation method before submitting.");
        var written = new List<string>();
        // Recheck ownership, workflow and the displayed version inside the same transaction as the write.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var request = form.Id == 0 ? new PurchaseRequest
            {
                DepartmentId = user.DepartmentId!.Value, CreatedByUserId = user.Id,
                CreatedAt = DateTime.UtcNow
            } : await Load(user.DepartmentId!.Value, form.Id)
                ?? throw new InvalidOperationException("The request is no longer available.");
            if (form.Id != 0 && (!RequestDisplay.Editable(request, user.Id) || Version(request) != form.Version))
                throw new InvalidOperationException("This request changed or can no longer be edited. Reload it before saving.");

            var oldSnapshot = form.Id == 0 ? null : JsonSerializer.Serialize(Snapshot(request));
            if (form.Id == 0) db.PurchaseRequests.Add(request);
            else db.PurchaseRequestItems.RemoveRange(request.Items);
            var department = await db.Departments.Where(d => d.Id == request.DepartmentId).Select(d => d.Name).SingleAsync();
            await db.ProcurementCategories.LoadAsync(); // category names for the stored snapshot
            request.Title = GenerateTitle(form.Items.Select(i => i.Name.Trim()).ToList(), department);
            request.Justification = form.Justification.Trim();
            request.Priority = form.Priority;
            request.EvaluationMethod = form.EvaluationMethod;
            request.NeededBy = form.NeededBy?.Date;
            request.Items = form.Items.Select(i => new PurchaseRequestItem
            {
                Name = i.Name.Trim(), Specifications = i.Specifications?.Trim(), CategoryId = i.CategoryId,
                Category = i.CategoryId is int c ? db.ProcurementCategories.Local.FirstOrDefault(x => x.Id == c) : null,
                Quantity = i.Quantity, Unit = i.Unit.Trim()
            }).ToList();

            foreach (var file in form.Attachments)
            {
                Directory.CreateDirectory(storageRoot);
                var name = Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant();
                var path = Path.Combine(storageRoot, name);
                written.Add(path);
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await file.CopyToAsync(output);
                request.Documents.Add(new Document
                {
                    FileName = Path.GetFileName(file.FileName), StoragePath = name,
                    ContentType = "application/octet-stream", SizeBytes = file.Length,
                    DocumentType = "DepartmentSupportingDocument", UploadedAt = DateTime.UtcNow,
                    UploadedByUserId = user.Id
                });
            }

            if (submit)
            {
                request.Status = PurchaseRequestStatus.Submitted;
                request.SubmittedAt = DateTime.UtcNow;
                request.Reviews.Add(new PurchaseRequestReview
                {
                    SubmissionNumber = request.Reviews.Select(r => r.SubmissionNumber).DefaultIfEmpty().Max() + 1,
                    SubmittedAt = request.SubmittedAt.Value,
                    SubmittedSnapshotJson = JsonSerializer.Serialize(Snapshot(request)),
                    Decision = ReviewDecision.Pending
                });
            }
            // A returned request remains Returned when corrections are saved, until explicit resubmission.
            await db.SaveChangesAsync();
            if (submit)
            {
                var officers = await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer);
                foreach (var officer in officers.Where(o => o.IsActive))
                    db.Notifications.Add(new Notification
                    {
                        UserId = officer.Id, Title = "Purchase request submitted",
                        Message = $"PR-{request.Id}: {request.Title}",
                        Link = $"/SourcingOfficer/PurchaseRequests/Review/{request.Id}", CreatedAt = DateTime.UtcNow
                    });
            }
            db.AuditLogs.Add(new AuditLog
            {
                UserId = user.Id, Action = submit ? "DepartmentSubmit" : "DepartmentSave",
                EntityName = nameof(PurchaseRequest), EntityId = request.Id.ToString(),
                OldValues = oldSnapshot, NewValues = JsonSerializer.Serialize(Snapshot(request)), CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return request.Id;
        }
        catch
        {
            foreach (var path in written) File.Delete(path);
            throw;
        }
    }
}
