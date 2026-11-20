using System.Data;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Private storage: never serve procurement files from wwwroot.
public sealed class SupplierDocumentService(ApplicationDbContext context, IWebHostEnvironment environment,
    SupplierCommunicationService communication, TimeProvider clock)
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
        string path;
        try { path = PrivateDocumentPaths.Resolve(environment.ContentRootPath, doc.StoragePath); }
        catch (WorkflowException) { return null; }
        if (!File.Exists(path)) return null;
        return (path, Path.GetFileName(doc.FileName), "application/octet-stream");
    }

    public async Task<string?> UploadAsync(string userId, IFormFile file, int? bidId = null, int? messageId = null, bool samplePhoto = false)
    {
        UploadFile upload;
        try
        {
            upload = await ReadUploadAsync(file);
            if (samplePhoto && (!bidId.HasValue || !upload.IsImage))
                return "Choose a PNG or JPEG image to attach as a sample photo.";
        }
        catch (WorkflowException e) { return e.Message; }

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var account = await communication.Accounts(userId).SingleOrDefaultAsync();
        if (account == null) return "Your approved supplier account is required.";
        var doc = NewDocument(userId, upload);
        if (bidId.HasValue)
        {
            var tenderId = await context.Bids.Where(b => b.Id == bidId).Select(b => (int?)b.TenderInvitation.TenderRound.TenderId).SingleOrDefaultAsync();
            if (!tenderId.HasValue) return "Draft offer is unavailable.";
            await context.Tenders.FromSqlInterpolated($"SELECT * FROM Tenders WITH (UPDLOCK,HOLDLOCK) WHERE Id={tenderId.Value}").SingleAsync();
            var now = clock.GetUtcNow().UtcDateTime;
            bool allowed = await context.Bids.AnyAsync(b => b.Id == bidId && b.Status == BidStatus.Draft
                && b.TenderInvitation.SupplierId == account.SupplierId && b.TenderInvitation.Status == InvitationStatus.Accepted
                && b.TenderInvitation.TenderRound.Status == TenderRoundStatus.Open
                && b.TenderInvitation.TenderRound.Tender.Status == TenderStatus.Bidding
                && b.TenderInvitation.TenderRound.Deadline > now
                && (!b.TenderInvitation.TenderRound.OpensAt.HasValue || b.TenderInvitation.TenderRound.OpensAt <= now));
            if (!allowed) return "Attachments can only be added to your open draft before the deadline.";
            doc.BidId = bidId;
            doc.DocumentType = samplePhoto ? Document.OfferSamplePhotoType : "OfferAttachment";
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
                Link = $"/Clarifications/Details/{thread.Id}", CreatedAt = clock.GetUtcNow().UtcDateTime
            });
        }
        else
        {
            doc.SupplierId = account.SupplierId; doc.DocumentType = "CompanyDocument";
            doc.VerificationStatus = DocumentVerificationStatus.Pending;
        }

        try
        {
            await StoreAsync(doc, upload);
            context.Documents.Add(doc);
            await context.SaveChangesAsync();
            AddUploadAudit(doc);
            await context.SaveChangesAsync();
            if (bidId.HasValue && !await context.Bids.AnyAsync(b => b.Id == bidId && b.Status == BidStatus.Draft && b.TenderInvitation.TenderRound.Deadline > clock.GetUtcNow().UtcDateTime))
                throw new WorkflowException("The attachment deadline has passed. Nothing was submitted.");
            await transaction.CommitAsync();
        }
        catch
        {
            DeleteStoredFiles([doc]);
            throw;
        }
        return null;
    }

    // The caller owns the tender lock and database transaction for the draft and its attachments.
    internal async Task<List<Document>> StoreOfferAttachmentsAsync(string userId, Bid bid, IReadOnlyList<IFormFile> files)
    {
        var documents = new List<Document>();
        try
        {
            foreach (var file in files)
            {
                var upload = await ReadUploadAsync(file);
                var doc = NewDocument(userId, upload);
                doc.Bid = bid;
                doc.DocumentType = upload.IsImage ? Document.OfferSamplePhotoType : "OfferAttachment";
                documents.Add(doc);
                await StoreAsync(doc, upload);
            }
            return documents;
        }
        catch
        {
            DeleteStoredFiles(documents);
            throw;
        }
    }

    internal void DeleteStoredFiles(IEnumerable<Document> documents)
    {
        foreach (var doc in documents)
        {
            var path = PrivateDocumentPaths.Resolve(environment.ContentRootPath, doc.StoragePath);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    internal void AddUploadAudit(Document doc) => context.AuditLogs.Add(new AuditLog
    {
        UserId = doc.UploadedByUserId, EntityName = nameof(Document), EntityId = doc.Id.ToString(),
        Action = "UploadSupplierDocument", CreatedAt = clock.GetUtcNow().UtcDateTime
    });

    private Document NewDocument(string userId, UploadFile upload) => new()
    {
        FileName = upload.Name, SizeBytes = upload.Bytes.Length, UploadedByUserId = userId, UploadedAt = clock.GetUtcNow().UtcDateTime,
        ContentType = upload.Extension == ".pdf" ? "application/pdf" : upload.Extension == ".png" ? "image/png" : "image/jpeg",
        StoragePath = Path.Combine("App_Data", "SupplierUploads", Guid.NewGuid().ToString("N") + upload.Extension)
    };

    private Task StoreAsync(Document doc, UploadFile upload)
    {
        Directory.CreateDirectory(StorageRoot);
        return File.WriteAllBytesAsync(Path.Combine(environment.ContentRootPath, doc.StoragePath), upload.Bytes);
    }

    private static async Task<UploadFile> ReadUploadAsync(IFormFile? file)
    {
        const long maxBytes = 10 * 1024 * 1024;
        if (file == null || file.Length is <= 0 or > maxBytes)
            throw new WorkflowException("Choose a file up to 10 MB.");
        var name = Path.GetFileName(file.FileName);
        if (name.Length == 0 || name.Length > 260) throw new WorkflowException("Invalid file name.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        var bytes = memory.ToArray();
        if (bytes.Length > maxBytes || !ValidSignature(bytes, extension))
            throw new WorkflowException($"{name}: choose a valid PDF, PNG or JPEG file.");
        return new UploadFile(name, extension, bytes);
    }

    private sealed record UploadFile(string Name, string Extension, byte[] Bytes)
    {
        public bool IsImage => Extension is ".png" or ".jpg" or ".jpeg";
    }

    private static bool ValidSignature(byte[] bytes, string extension) => extension switch
    {
        ".pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
        ".png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
        _ => false
    };
}
