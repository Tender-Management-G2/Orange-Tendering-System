using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Orange_Tendering_System.Services.Assistant;

public sealed class AiFileException(string message, int status = 400) : Exception(message) { public int Status { get; } = status; }
public sealed record AiKnowledgeSourceText(string FileName, string Format, string Sha256, string Text);

// Generated reports and uploaded documents of the AI assistant. Files live in App_Data/AiAssistant (outside wwwroot) under random
// names; every read checks the owning UserId and expiry on the server. Uploaded content is parsed as data only: nothing is executed,
// macros/embedded objects are never opened, and extracted text is bounded.
public sealed class AiChatFileStore(IWebHostEnvironment environment, ApplicationDbContext db, TimeProvider clock, ILogger<AiChatFileStore> logger)
{
    static AiChatFileStore() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    public const int MaxExtractedChars = 60_000, MaxGeneratedPerTenMinutes = 10, MaxUploadsPerHour = 12;
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly Dictionary<string, (string Format, string ContentType)> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("pdf", "application/pdf"),
        [".xlsx"] = ("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        [".docx"] = ("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        [".pptx"] = ("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        [".csv"] = ("csv", "text/csv"),
        [".txt"] = ("txt", "text/plain"),
    };
    public static readonly string[] UploadExtensions = [".pdf", ".xlsx", ".docx", ".csv", ".txt"];

    private string Root => Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "AiAssistant"));
    private string PathFor(string key)
    {
        if (key.Length is < 36 or > 37 || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '.'))) throw new AiFileException("Invalid file reference.", 404);
        var path = Path.GetFullPath(Path.Combine(Root, key));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new AiFileException("Invalid file reference.", 404);
        return path;
    }

    public static object Card(AiChatFile f) => new
    {
        id = f.Id, fileName = f.FileName, format = f.Format, sizeBytes = f.SizeBytes, createdAt = f.CreatedAt, expiresAt = f.ExpiresAt,
        kind = f.Kind, downloadUrl = f.Kind == "generated" ? "/AiChat/Download/" + f.Id : null
    };

    public async Task EnsureGenerationAllowedAsync(string userId, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
        if (await db.AiChatFiles.CountAsync(f => f.UserId == userId && f.Kind == "generated" && f.CreatedAt > since, ct) >= MaxGeneratedPerTenMinutes)
            throw new AiFileException("You have generated many files in the last few minutes. Please wait a little and try again.", 429);
    }

    public async Task<AiChatFile> SaveGeneratedAsync(string userId, int conversationId, string title, string extension, byte[] content, CancellationToken ct)
    {
        var type = Types[extension];
        var safe = SafeName(title);
        var file = await Write(userId, conversationId, "generated", $"{safe}_{JordanTime.Now:yyyyMMdd-HHmm}{extension}", extension, type, content, null, ct);
        Audit(userId, "AiReportGenerated", file, new { file.Format, file.SizeBytes });
        await db.SaveChangesAsync(ct);
        return file;
    }

    private async Task<AiChatFile> Write(string userId, int? conversationId, string kind, string fileName, string extension, (string Format, string ContentType) type, byte[] content, string? text, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var id = Guid.NewGuid();
        var key = id.ToString("N") + extension.ToLowerInvariant();
        var path = PathFor(key);
        await File.WriteAllBytesAsync(path, content, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var file = new AiChatFile
        {
            Id = id, UserId = userId, ConversationId = conversationId, Kind = kind, FileName = fileName.Length > 200 ? fileName[^200..] : fileName, StorageKey = key,
            ContentType = type.ContentType, Format = type.Format, SizeBytes = content.LongLength, Sha256 = Convert.ToHexString(SHA256.HashData(content)),
            ExtractedText = text, CreatedAt = now, ExpiresAt = now + Retention
        };
        db.AiChatFiles.Add(file);
        return file;
    }

    // Download: the owner only, before expiry, and only while the file still exists on disk.
    public async Task<(AiChatFile File, string Path)> OpenForDownloadAsync(string userId, Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var file = await db.AiChatFiles.SingleOrDefaultAsync(f => f.Id == id && f.UserId == userId && f.Kind == "generated" && f.DeletedAt == null, ct)
            ?? throw new AiFileException("File not found.", 404);
        if (file.ExpiresAt <= now) throw new AiFileException("This file has expired. Ask the assistant to generate it again.", 410);
        var path = PathFor(file.StorageKey);
        if (!File.Exists(path)) throw new AiFileException("File not found.", 404);
        Audit(userId, "AiFileDownloaded", file, new { file.Format });
        await db.SaveChangesAsync(ct);
        return (file, path);
    }

    public async Task<AiChatFile> UploadAsync(string userId, int conversationId, IFormFile upload, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddHours(-1);
        if (await db.AiChatFiles.CountAsync(f => f.UserId == userId && f.Kind == "upload" && f.CreatedAt > since, ct) >= MaxUploadsPerHour)
            throw new AiFileException("Upload limit reached for this hour. Please try again later.", 429);
        var name = Path.GetFileName(upload.FileName ?? "");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!UploadExtensions.Contains(extension)) throw new AiFileException("Supported files: PDF, Excel (.xlsx), Word (.docx), CSV and TXT.");
        if (upload.Length <= 0 || upload.Length > MaxUploadBytes) throw new AiFileException("Files must be between 1 byte and 10 MB.");
        byte[] content;
        using (var buffer = new MemoryStream((int)upload.Length))
        {
            await upload.CopyToAsync(buffer, ct);
            content = buffer.ToArray();
        }
        string text;
        try { text = Extract(extension, content); }
        catch (AiFileException) { throw; }
        catch (Exception e) when (e is InvalidDataException or XmlException or IOException or FormatException or InvalidOperationException or ArgumentException or NotSupportedException
                                  || e.GetType().Namespace?.StartsWith("UglyToad") == true || e.GetType().Namespace?.StartsWith("ClosedXML") == true)
        {
            logger.LogInformation(e, "AI assistant upload rejected: {Extension} could not be parsed", extension);
            throw new AiFileException("The file could not be read. It may be damaged, password-protected or not the type its extension says.");
        }
        var file = await Write(userId, conversationId, "upload", name, extension, Types[extension], content, text, ct);
        Audit(userId, "AiFileUploaded", file, new { file.Format, file.SizeBytes });
        await db.SaveChangesAsync(ct);
        return file;
    }

    internal async Task<(string Name, byte[] Content)> ReadConversationUploadAsync(string userId, int conversationId, Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var upload = await db.AiChatFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id && f.UserId == userId
            && f.ConversationId == conversationId && f.Kind == "upload" && f.DeletedAt == null && f.ExpiresAt > now, ct)
            ?? throw new AiFileException("The upload is no longer available in this chat.", 404);
        var path = PathFor(upload.StorageKey);
        if (!File.Exists(path)) throw new AiFileException("The upload is no longer available in this chat.", 404);
        return (upload.FileName, await File.ReadAllBytesAsync(path, ct));
    }

    // Reuses the same safe PDF/Office/text parser for persistent knowledge previews without writing the file to disk.
    public async Task<AiKnowledgeSourceText> ExtractForKnowledgeAsync(IFormFile upload, CancellationToken ct)
    {
        var name = Path.GetFileName(upload.FileName ?? "");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!UploadExtensions.Contains(extension)) throw new AiFileException("Supported knowledge files: PDF, Excel (.xlsx), Word (.docx), CSV and TXT.");
        if (upload.Length <= 0 || upload.Length > MaxUploadBytes) throw new AiFileException("Files must be between 1 byte and 10 MB.");
        byte[] content;
        using (var buffer = new MemoryStream((int)upload.Length)) { await upload.CopyToAsync(buffer, ct); content = buffer.ToArray(); }
        try
        {
            var text = Extract(extension, content);
            if (text.Length < 20) throw new AiFileException("The file does not contain enough readable text to index.");
            return new(name, extension[1..], Convert.ToHexString(SHA256.HashData(content)), text);
        }
        catch (AiFileException) { throw; }
        catch (Exception e) when (e is InvalidDataException or XmlException or IOException or FormatException or InvalidOperationException or ArgumentException or NotSupportedException
                                  || e.GetType().Namespace?.StartsWith("UglyToad") == true || e.GetType().Namespace?.StartsWith("ClosedXML") == true)
        {
            logger.LogInformation(e, "AI knowledge preview rejected: {Extension} could not be parsed", extension);
            throw new AiFileException("The file could not be read. It may be damaged, password-protected or not the type its extension says.");
        }
    }

    public async Task<List<AiChatFile>> ConversationUploadsAsync(string userId, int conversationId, CancellationToken ct) =>
        await db.AiChatFiles.Where(f => f.UserId == userId && f.ConversationId == conversationId && f.Kind == "upload" && f.DeletedAt == null && f.ExpiresAt > clock.GetUtcNow().UtcDateTime)
            .OrderByDescending(f => f.CreatedAt).Take(3).ToListAsync(ct);

    // Re-reads a structured upload (CSV/XLSX) as a table for conversion to a report. Bounded to 2,000 rows x 30 columns.
    public AssistantDataset? TableFromUpload(AiChatFile file, bool arabic)
    {
        if (file.Format is not ("csv" or "xlsx")) return null;
        var path = PathFor(file.StorageKey);
        if (!File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        var rows = file.Format == "csv" ? ParseCsv(DecodeText(bytes), 2001, 30) : ReadSheet(bytes, 2001, 30);
        if (rows.Count == 0) return null;
        var ds = new AssistantDataset { Topic = AssistantTopic.None, Title = (arabic ? "بيانات من الملف: " : "Data from: ") + file.FileName, Arabic = arabic, ScopeNote = arabic ? "محتوى ملف رفعه المستخدم (غير متحقق منه)" : "User-uploaded file content (not verified by the system)" };
        var header = rows[0]; var width = rows.Max(r => r.Length);
        for (var c = 0; c < width; c++) ds.Columns.Add(new(c < header.Length && header[c].Trim().Length > 0 ? header[c].Trim() : $"Column {c + 1}", ColumnKind.Text, 16));
        foreach (var row in rows.Skip(1).Take(2000)) ds.Rows.Add(Enumerable.Range(0, width).Select(c => (object?)(c < row.Length ? row[c] : null)).ToArray());
        ds.TotalCount = ds.Rows.Count; ds.Truncated = rows.Count > 2001;
        return ds;
    }

    // ---- validation and text extraction (no code, macros or external references are ever executed or resolved) ----
    private static string Extract(string extension, byte[] content)
    {
        switch (extension)
        {
            case ".pdf":
            {
                if (content.AsSpan(0, Math.Min(1024, content.Length)).IndexOf("%PDF-"u8) < 0) throw new AiFileException("This is not a valid PDF file.");
                using var pdf = PdfDocument.Open(content, new ParsingOptions { UseLenientParsing = true });
                var sb = new StringBuilder();
                foreach (var page in pdf.GetPages())
                {
                    if (page.Number > 200 || sb.Length > MaxExtractedChars)
                    {
                        sb.AppendLine("\n[truncated]");
                        break;
                    }
                    sb.Append($"\n[Page {page.Number}]\n").Append(ContentOrderTextExtractor.GetText(page));
                }
                var text = Bound(sb.ToString());
                if (text.Count(char.IsLetterOrDigit) < 20)
                    throw new AiFileException("This PDF has no readable text layer (it looks scanned). OCR is not available, so it cannot be analyzed.");
                return text;
            }
            case ".docx":
            {
                using var zip = OpenZip(content, "word/document.xml");
                var entry = zip.GetEntry("word/document.xml")!;
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, SafeXml);
                var doc = XDocument.Load(reader);
                var paragraphs = doc.Descendants().Where(x => x.Name.LocalName == "p")
                    .Select(p => string.Concat(p.Descendants().Where(x => x.Name.LocalName is "t" or "tab").Select(x => x.Name.LocalName == "tab" ? "\t" : x.Value)));
                return Bound(string.Join("\n", paragraphs.Where(p => p.Trim().Length > 0)));
            }
            case ".xlsx":
            {
                using (OpenZip(content, "xl/workbook.xml")) { }
                var rows = ReadSheet(content, 1500, 40, allSheets: true);
                return Bound(string.Join("\n", rows.Select(r => string.Join("\t", r))));
            }
            default:
            {
                if (content.Contains((byte)0)) throw new AiFileException("This file contains binary data and is not a valid text/CSV file.");
                return Bound(DecodeText(content));
            }
        }
    }

    private static readonly XmlReaderSettings SafeXml = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 };

    // Open XML packages: must be a real ZIP with the expected main part, no VBA project, and bounded expansion (zip-bomb guard).
    private static ZipArchive OpenZip(byte[] content, string requiredEntry)
    {
        if (content.Length < 4 || content[0] != 'P' || content[1] != 'K') throw new AiFileException("This is not a valid Office Open XML file.");
        var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        if (zip.Entries.Count > 3000 || zip.Entries.Sum(e => e.Length) > 150L * 1024 * 1024) { zip.Dispose(); throw new AiFileException("The file expands to an unsafe size and was rejected."); }
        if (zip.GetEntry(requiredEntry) == null) { zip.Dispose(); throw new AiFileException("The file content does not match its extension."); }
        if (zip.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))) { zip.Dispose(); throw new AiFileException("Files containing macros are not accepted."); }
        return zip;
    }

    private static List<string[]> ReadSheet(byte[] content, int maxRows, int maxCols, bool allSheets = false)
    {
        using var book = new XLWorkbook(new MemoryStream(content));
        var rows = new List<string[]>();
        foreach (var sheet in book.Worksheets.Take(allSheets ? 5 : 1))
        {
            if (allSheets) rows.Add([$"[Sheet: {sheet.Name}]"]);
            var used = sheet.RangeUsed();
            if (used == null) continue;
            var lastCol = Math.Min(used.LastColumn().ColumnNumber(), maxCols);
            foreach (var row in used.Rows().Take(maxRows))
                // CachedValue: the stored result only; formulas are never recalculated or evaluated here.
                rows.Add(Enumerable.Range(1, lastCol).Select(c => { var cell = row.Cell(c); var v = cell.HasFormula ? cell.CachedValue : cell.Value; return v.ToString(System.Globalization.CultureInfo.InvariantCulture); }).ToArray());
        }
        if (allSheets && (book.Worksheets.Count > 5 || book.Worksheets.Take(5).Any(sheet =>
            sheet.RangeUsed() is { } used && (used.RowCount() > maxRows || used.LastColumn().ColumnNumber() > maxCols))))
            rows.Add(["[truncated]"]);
        return rows;
    }

    private static string DecodeText(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding(1256).GetString(bytes); }   // Arabic Windows code page (common for Excel CSV exports)
            catch (ArgumentException) { return Encoding.Latin1.GetString(bytes); }
        }
    }

    private static List<string[]> ParseCsv(string text, int maxRows, int maxCols)
    {
        var rows = new List<string[]>();
        var delimiter = text.Split('\n').FirstOrDefault()?.Count(c => c == ';') > text.Split('\n').FirstOrDefault()?.Count(c => c == ',') ? ';' : ',';
        var field = new StringBuilder(); var row = new List<string>(); var quoted = false;
        for (var i = 0; i < text.Length && rows.Count < maxRows; i++)
        {
            var c = text[i];
            if (quoted) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else quoted = false; } else field.Append(c); continue; }
            if (c == '"') quoted = true;
            else if (c == delimiter) { if (row.Count < maxCols) row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n') { if (row.Count < maxCols) row.Add(field.ToString().TrimEnd('\r')); field.Clear(); if (row.Any(x => x.Length > 0)) rows.Add([.. row]); row.Clear(); }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); if (rows.Count < maxRows) rows.Add([.. row]); }
        return rows;
    }

    private static string Bound(string text)
    {
        text = new string(text.Where(c => c is '\n' or '\t' || !char.IsControl(c)).ToArray()).Trim();
        return text.Length <= MaxExtractedChars ? text : text[..MaxExtractedChars] + "\n[truncated]";
    }

    private static string SafeName(string title)
    {
        var cleaned = new string(title.Select(c => char.IsLetterOrDigit(c) ? c : c is ' ' or '-' or '_' ? '_' : '_').ToArray());
        while (cleaned.Contains("__")) cleaned = cleaned.Replace("__", "_");
        cleaned = cleaned.Trim('_');
        return cleaned.Length == 0 ? "Orange_Report" : cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }

    private void Audit(string userId, string action, AiChatFile file, object values) =>
        db.AuditLogs.Add(new AuditLog { UserId = userId, EntityName = "AiAssistantFile", EntityId = file.Id.ToString(), Action = action, NewValues = System.Text.Json.JsonSerializer.Serialize(values), CreatedAt = clock.GetUtcNow().UtcDateTime });

    // Retention: expired files are deleted from disk and their extracted text is cleared; the audit trail remains.
    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expired = await db.AiChatFiles.Where(f => f.DeletedAt == null && f.ExpiresAt <= now).OrderBy(f => f.ExpiresAt).Take(500).ToListAsync(ct);
        foreach (var f in expired)
        {
            try { var path = PathFor(f.StorageKey); if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or AiFileException) { logger.LogWarning(e, "Could not delete expired AI assistant file {FileId}", f.Id); continue; }
            f.DeletedAt = now; f.ExtractedText = null;
        }
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }

    public async Task DetachConversationAsync(string userId, int conversationId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.AiChatFiles.Where(f => f.UserId == userId && f.ConversationId == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ConversationId, (int?)null).SetProperty(f => f.ExpiresAt, f => f.ExpiresAt < now ? f.ExpiresAt : now), ct);
    }
}

public sealed class AiChatFileCleanupWorker(IServiceScopeFactory scopes, ILogger<AiChatFileCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var removed = await scope.ServiceProvider.GetRequiredService<AiChatFileStore>().CleanupAsync(stoppingToken);
                if (removed > 0) logger.LogInformation("Removed {Count} expired AI assistant files", removed);
            }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "AI assistant file cleanup failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
