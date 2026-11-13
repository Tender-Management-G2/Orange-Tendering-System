using System.Data;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Private storage: never serve procurement files from wwwroot.
public sealed class SupplierDocumentService(ApplicationDbContext context, IWebHostEnvironment environment,
    SupplierCommunicationService communication)
{
    private string StorageRoot => Path.Combine(environment.ContentRootPath, "App_Data", "SupplierUploads");

    private IQueryable<Document> OwnDocuments(string userId) => context.Documents.Where(d =>
        communication.Accounts(userId).Any(u =>
            d.SupplierId == u.SupplierId || (d.Bid != null && d.Bid.TenderInvitation.SupplierId == u.SupplierId)
            || (d.TenderId != null && context.TenderInvitations.Any(i => i.SupplierId == u.SupplierId && i.TenderRound.TenderId == d.TenderId)))
        || (d.ClarificationMessageId != null && communication.Threads(userId)
            .Any(c => c.Messages.Any(m => m.Id == d.ClarificationMessageId))));

    public async Task<List<SupplierDocumentViewModel>> GetCompanyDocumentsAsync(string userId) => await OwnDocuments(userId)
        .AsNoTracking().Where(d => d.SupplierId != null).OrderByDescending(d => d.UploadedAt)
        .Select(d => new SupplierDocumentViewModel
        { Id = d.Id, FileName = d.FileName, DocumentType = d.DocumentType, SizeBytes = d.SizeBytes, UploadedAt = d.UploadedAt }).ToListAsync();

    public async Task<(string Path, string Name, string Type)?> GetDownloadAsync(string userId, int id)
    {
        var doc = await OwnDocuments(userId).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id);
        if (doc == null) return null;
        var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, doc.StoragePath));
        // Only private App_Data paths are allowed, including company files created by the team.
        var privateRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(privateRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        return (path, Path.GetFileName(doc.FileName), "application/octet-stream");
    }

    public async Task<string?> UploadAsync(string userId, IFormFile file, int? bidId = null, int? messageId = null)
    {
        if (file == null || file.Length <= 0 || file.Length > 10 * 1024 * 1024)
            return "Choose a file up to 10 MB.";
        var name = Path.GetFileName(file.FileName);
        if (name.Length == 0 || name.Length > 260) return "Invalid file name.";
        var extension = Path.GetExtension(name).ToLowerInvariant();
        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        var bytes = memory.ToArray();
        if (bytes.Length > 10 * 1024 * 1024 || !ValidSignature(bytes, extension))
            return "Only valid PDF, PNG or JPEG files are allowed.";

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var account = await communication.Accounts(userId).SingleOrDefaultAsync();
        if (account == null) return "Your approved supplier account is required.";
        var doc = new Document
        {
            FileName = name, SizeBytes = bytes.Length, UploadedByUserId = userId, UploadedAt = DateTime.UtcNow,
            ContentType = extension == ".pdf" ? "application/pdf" : extension == ".png" ? "image/png" : "image/jpeg"
        };
        if (bidId.HasValue)
        {
            var now = DateTime.UtcNow;
            bool allowed = await context.Bids.AnyAsync(b => b.Id == bidId && b.Status == BidStatus.Draft
                && b.TenderInvitation.SupplierId == account.SupplierId && b.TenderInvitation.Status == InvitationStatus.Accepted
                && b.TenderInvitation.TenderRound.Status == TenderRoundStatus.Open
                && b.TenderInvitation.TenderRound.Tender.Status == TenderStatus.Bidding
                && b.TenderInvitation.TenderRound.Deadline > now
                && (!b.TenderInvitation.TenderRound.OpensAt.HasValue || b.TenderInvitation.TenderRound.OpensAt <= now));
            if (!allowed) return "Attachments can only be added to your open draft before the deadline.";
            doc.BidId = bidId; doc.DocumentType = "OfferAttachment";
        }
        else if (messageId.HasValue)
        {
            var message = await context.ClarificationMessages.SingleOrDefaultAsync(m => m.Id == messageId
                && m.SenderUserId == userId && communication.Threads(userId)
                    .Any(c => c.Id == m.ClarificationId && c.Status != ClarificationStatus.Closed));
            if (message == null) return "This clarification message is unavailable or resolved.";
            doc.ClarificationMessageId = messageId; doc.DocumentType = "ClarificationAttachment";
            var thread = await context.Clarifications.SingleAsync(c => c.Id == message.ClarificationId);
            context.Notifications.Add(new Notification
            {
                UserId = thread.FromUserId == userId ? thread.ToUserId : thread.FromUserId,
                Title = "Supplier clarification attachment", Message = $"A document was added to clarification #{thread.Id}.",
                Link = $"/Clarifications/Details/{thread.Id}", CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            doc.SupplierId = account.SupplierId; doc.DocumentType = "CompanyDocument";
            doc.VerificationStatus = DocumentVerificationStatus.Pending;
        }

        Directory.CreateDirectory(StorageRoot);
        var relative = Path.Combine("App_Data", "SupplierUploads", Guid.NewGuid().ToString("N") + extension);
        var fullPath = Path.Combine(environment.ContentRootPath, relative);
        doc.StoragePath = relative;
        try
        {
            await File.WriteAllBytesAsync(fullPath, bytes);
            context.Documents.Add(doc);
            await context.SaveChangesAsync();
            context.AuditLogs.Add(new AuditLog
            {
                UserId = userId, EntityName = nameof(Document), EntityId = doc.Id.ToString(),
                Action = "UploadSupplierDocument", CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }
        return null;
    }

    private static bool ValidSignature(byte[] bytes, string extension) => extension switch
    {
        ".pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
        ".png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
        _ => false
    };
}
