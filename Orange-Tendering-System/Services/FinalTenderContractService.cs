using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Reports;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

public sealed class FinalTenderContractService(ApplicationDbContext db, IWebHostEnvironment environment, AuditLogService audit)
{
    private string DirectoryPath => Path.Combine(environment.ContentRootPath, "App_Data", "FinalContracts");

    private IQueryable<Tender> AwardedTenders() => db.Tenders.AsNoTracking()
        .Where(t => t.Status == TenderStatus.Awarded && t.AwardedBidId != null);

    public Task<List<AnalyticsOption>> TenderOptionsAsync(CancellationToken ct) => AwardedTenders()
        .OrderByDescending(t => t.AwardedAt).ThenByDescending(t => t.Id).Select(t => new AnalyticsOption(t.Id, t.Title)).ToListAsync(ct);

    public async Task<bool> SaveAsync(int tenderId, IFormFile pdf, string actorId, CancellationToken ct)
    {
        if (!await AwardedTenders().AnyAsync(t => t.Id == tenderId, ct)) return false;
        using var stream = new MemoryStream((int)pdf.Length);
        await pdf.CopyToAsync(stream, ct);
        var bytes = stream.ToArray();
        var hash = ReportCryptography.Hash(bytes);
        if (await db.FinalTenderContracts.AnyAsync(c => c.TenderId == tenderId && c.Sha256 == hash, ct)) return true;

        var contract = new FinalTenderContract
        {
            TenderId = tenderId, FileName = Path.GetFileName(pdf.FileName), Sha256 = hash, StorageKey = Guid.NewGuid().ToString("N"),
            SizeBytes = bytes.Length, UploadedByUserId = actorId, UploadedAt = audit.UtcNow
        };
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, contract.StorageKey + ".pdf");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            await using (file) await file.WriteAsync(bytes, ct);
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        db.FinalTenderContracts.Add(contract);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId, EntityName = nameof(Tender), EntityId = tenderId.ToString(CultureInfo.InvariantCulture),
            Action = "FinalContractUploaded", NewValues = JsonSerializer.Serialize(new { contract.FileName, contract.Sha256, contract.SizeBytes }),
            CreatedAt = contract.UploadedAt
        });
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateException or OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            var stored = await db.FinalTenderContracts.AsNoTracking().Where(c => c.TenderId == tenderId && c.Sha256 == hash)
                .Select(c => c.StorageKey).SingleOrDefaultAsync(CancellationToken.None);
            if (stored != contract.StorageKey) File.Delete(path);
            if (stored == null || exception is OperationCanceledException) throw;
        }
        return true;
    }

    public async Task<(byte[] Bytes, string Name)?> DownloadAsync(int id, string actorId, CancellationToken ct)
    {
        var contract = await db.FinalTenderContracts.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id && c.Tender.Status == TenderStatus.Awarded && c.Tender.AwardedBidId != null, ct);
        if (contract == null || !Guid.TryParseExact(contract.StorageKey, "N", out _)) return null;
        var path = Path.Combine(DirectoryPath, contract.StorageKey + ".pdf");
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(path, ct); }
        catch (IOException exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return null; }
        if (ReportCryptography.Hash(bytes) != contract.Sha256) throw new WorkflowException("The stored contract failed its integrity check.");
        await audit.RecordAsync(actorId, "FinalContractDownloaded", nameof(FinalTenderContract), id.ToString(CultureInfo.InvariantCulture),
            new { contract.TenderId, contract.FileName, contract.Sha256 });
        return (bytes, contract.FileName);
    }
}
