using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using System.Security.Claims;
using System.Data;

namespace Orange_Tendering_System.Services;

public class ProtectedDocumentStore(IWebHostEnvironment environment, ApplicationDbContext db, TenderWorkflowService workflow)
{
    private string Root => Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "TenderDocuments"));
    private static readonly string[] Extensions = [".pdf", ".txt", ".csv", ".docx", ".png", ".jpg", ".jpeg"];
    public string Resolve(Document document) => PrivateDocumentPaths.Resolve(environment.ContentRootPath, document.StoragePath);
    public async Task Upload(int tenderId, int? bidId, IFormFile file, ClaimsPrincipal principal, CancellationToken ct)
    {
        var a = await workflow.Actor(principal);
        TenderWorkflowService.Require(file.Length is > 0 and <= 20 * 1024 * 1024 && Extensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()), "Allowed: PDF, TXT, CSV, DOCX, PNG and JPEG; maximum 20 MB.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var t = await db.Tenders.FromSqlInterpolated($"SELECT * FROM Tenders WITH (UPDLOCK,HOLDLOCK) WHERE Id={tenderId}").SingleOrDefaultAsync(ct);
        TenderWorkflowService.Require(t != null, "Tender not found.");
        DateTime? deadline = null;
        if (bidId.HasValue)
        {
            var bid = await db.Bids.Include(x => x.TenderInvitation).ThenInclude(x => x.TenderRound).SingleOrDefaultAsync(x => x.Id == bidId, ct);
            TenderWorkflowService.Require(bid?.TenderInvitation.TenderRound.TenderId == tenderId && bid.TenderInvitation.SupplierId == a.SupplierId && await workflow.Role(a, SystemRoles.Supplier), "Only the owning supplier may upload bid documents.");
            deadline = bid!.TenderInvitation.TenderRound.Deadline;
            TenderWorkflowService.Require(bid.Status == BidStatus.Submitted && bid.TenderInvitation.TenderRound.Status == TenderRoundStatus.Open && deadline > workflow.Now, "Document submission is closed.");
        }
        else TenderWorkflowService.Require(await workflow.IsOfficer(a) && t.Status == TenderStatus.Draft && !await db.Set<WeightRound>().AnyAsync(x => x.TenderId == tenderId, ct), "Requirement documents are frozen after the weight request.");
        Directory.CreateDirectory(Root);
        var key = Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant();
        var document = new Document { TenderId = bidId == null ? tenderId : null, BidId = bidId, FileName = Path.GetFileName(file.FileName), StoragePath = key,
            ContentType = "application/octet-stream", SizeBytes = file.Length, UploadedByUserId = a.Id, UploadedAt = workflow.Now };
        var path = Resolve(document);
        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await file.CopyToAsync(stream, ct);
            db.Documents.Add(document);
            db.AuditLogs.Add(new AuditLog { UserId = a.Id, EntityName = "Tender", EntityId = tenderId.ToString(), Action = "DocumentUploaded", NewValues = System.Text.Json.JsonSerializer.Serialize(new { document.FileName, bidId }), CreatedAt = workflow.Now });
            await db.SaveChangesAsync(ct);
            TenderWorkflowService.Require(deadline == null || workflow.Now < deadline, "Document submission deadline passed.");
            await tx.CommitAsync(ct);
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    public async Task<string> Extract(Document d, CancellationToken ct)
    {
        var path = Resolve(d);
        if (!File.Exists(path)) return "[File unavailable; no content inferred]";
        var extension = Path.GetExtension(d.FileName).ToLowerInvariant();
        if (extension is ".txt" or ".csv")
        {
            using var reader = new StreamReader(path, Encoding.UTF8, true);
            var buffer = new char[16000]; var count = await reader.ReadAsync(buffer.AsMemory(), ct);
            return new string(buffer, 0, count) + (count < buffer.Length ? "" : "\n[extraction limited to 16000 characters]");
        }
        if (extension == ".docx")
        {
            using var zip = ZipFile.OpenRead(path); var entry = zip.GetEntry("word/document.xml");
            if (entry == null || entry.Length > 2 * 1024 * 1024) return "[DOCX content missing or exceeds safe extraction limit]";
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
            var text = string.Join(" ", XDocument.Load(reader).Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value));
            return text.Length <= 16000 ? text : text[..16000] + " [truncated]";
        }
        return "[No trusted extractor configured for this file type; review the original document manually. No content inferred.]";
    }
}
