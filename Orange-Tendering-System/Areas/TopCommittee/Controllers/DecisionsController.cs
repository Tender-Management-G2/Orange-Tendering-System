using Microsoft.AspNetCore.Mvc.Razor;
using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = SystemRoles.TopCommittee)]
public sealed class DecisionsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IWebHostEnvironment environment, ILogger<DecisionsController> logger, IRazorViewEngine views) : TopCommitteeControllerBase(db, users)
{
    public async Task<IActionResult> ReviewEvaluation(int? id, string? search = null, CommitteeRoundStatus? status = null, int page = 1)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        if (!id.HasValue) return await SelectRound(user.Id, nameof(ReviewEvaluation), search, status, page);
        var details = await Committee.Load(user.Id, id.Value);
        return details is null ? NotFound() : View(details);
    }
    public async Task<IActionResult> Decision(int? id, string? search = null, CommitteeRoundStatus? status = null, int page = 1)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        Response.Headers.CacheControl = "no-store";
        if (!id.HasValue) return await SelectRound(user.Id, nameof(Decision), search, status, page);
        var details = await Committee.Load(user.Id, id.Value);
        if (details?.Round.TenderReport is { ReportType: ReportType.ExecutiveSummary, SubmittedAt: not null, Status: ReportStatus.Submitted })
        {
            try
            {
                await Committee.NotifyReportReady(user.Id, id.Value);
                details = await Committee.Load(user.Id, id.Value);
            }
            catch (InvalidOperationException exception) { TempData["TopError"] = exception.Message; }
        }
        return details is null ? NotFound() : View(details);
    }

    private async Task<IActionResult> SelectRound(string userId, string targetAction, string? search, CommitteeRoundStatus? status, int page)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value)) return BadRequest();
        Response.Headers.CacheControl = "no-store";
        search = search?.Trim();
        var query = TopCommitteeSearch.Apply(Committee.RoundList(userId).AsNoTracking(), search, status);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20m)));
        var model = new RoundSelection
        {
            TargetAction = targetAction, UserId = userId,
            Listing = new DecisionHistory
            {
                Search = search, Status = status, Total = total, Page = page,
                Rounds = await query.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
                    .Skip((page - 1) * 20).Take(20).AsSplitQuery().ToListAsync()
            }
        };
        return Request.Headers["X-TopCommittee-Live"] == "1" ? PartialView("_RoundResults", model) : View("SelectRound", model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Vote(VoteInput input)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        if (input.Decision != VoteDecision.Reject)
        {
            TempData["TopError"] = "Use Sign & Approve Report to record an approval. The signature is the approval.";
            return RedirectToAction(nameof(Decision), new { id = input.RoundId });
        }
        if (input.Decision == VoteDecision.Reject && string.IsNullOrWhiteSpace(input.Comment))
            ModelState.AddModelError(nameof(input.Comment), "A written rejection reason is required.");
        if (!ModelState.IsValid)
        {
            var details = await Committee.Load(user.Id, input.RoundId);
            return details is null ? NotFound() : View("Decision", details);
        }
        try
        {
            await Committee.Vote(user.Id, input);
            TempData["TopMessage"] = "Your vote was saved. The final result is processed automatically after all four members vote.";
        }
        catch (InvalidOperationException exception) { TempData["TopError"] = exception.Message; }
        catch (Exception exception) when (exception is DbUpdateException or System.Data.Common.DbException)
        {
            logger.LogWarning(exception, "Committee vote could not be committed");
            TempData["TopError"] = "The vote could not be committed. Refresh to check the latest votes before retrying.";
        }
        return RedirectToAction(nameof(Decision), new { id = input.RoundId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SignReport(int roundId, string? signatureData, CancellationToken cancellationToken)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        if (!TryBuildElectronicSignatureSvg(signatureData, out var signatureSvg))
        {
            TempData["TopError"] = "Draw your electronic signature in the signing area before approving the report.";
            return RedirectToAction(nameof(Decision), new { id = roundId });
        }
        try
        {
            var details = await Committee.Load(user.Id, roundId);
            if (details is null) return NotFound();
            if (!details.CanVote) throw new InvalidOperationException(string.Join(" ", details.Blockers));
            var original = await GetOrCreateOriginalPdf(details, user.Id, cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(original));
            await Committee.Vote(user.Id, new VoteInput { RoundId = roundId, Decision = VoteDecision.Approve },
                signatureHash: hash, signatureSvg: signatureSvg,
                finalPdfFactory: async (round, token) =>
                {
                    var sourceHtml = await ReadOriginalHtml(round, token)
                        ?? throw new InvalidOperationException("The preserved original report source is unavailable or failed its integrity check.");
                    var appendix = await ReportingExport.HtmlView(this, views, round,
                        "/Areas/TopCommittee/Views/Decisions/SignatureAppendix.cshtml");
                    var bodyEnd = sourceHtml.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    if (bodyEnd < 0) throw new InvalidOperationException("The preserved original report is malformed.");
                    return await BrowserReportPdf.Create(sourceHtml.Insert(bodyEnd, appendix), environment.ContentRootPath, token);
                }, reportStorageRoot: ReportStorageRoot);
            TempData["TopMessage"] = "Your signature and approval were recorded for this exact report version.";
        }
        catch (InvalidOperationException exception) { TempData["TopError"] = exception.Message; }
        catch (Exception exception) when (exception is DbUpdateException or System.Data.Common.DbException or IOException)
        {
            logger.LogWarning(exception, "Top Committee report signature could not be committed for round {RoundId}", roundId);
            TempData["TopError"] = "The signature could not be committed. Refresh the report and check the latest signature status before retrying.";
        }
        return RedirectToAction(nameof(Decision), new { id = roundId });
    }

    private static bool TryBuildElectronicSignatureSvg(string? json, out string svg)
    {
        svg = string.Empty;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 100_000) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() is < 1 or > 20) return false;
            var paths = new StringBuilder();
            var totalPoints = 0;
            var totalDistance = 0d;
            foreach (var stroke in document.RootElement.EnumerateArray())
            {
                if (stroke.ValueKind != JsonValueKind.Array || stroke.GetArrayLength() is < 2 or > 1000) return false;
                var path = new StringBuilder();
                var first = true;
                double lastX = 0, lastY = 0;
                foreach (var point in stroke.EnumerateArray())
                {
                    if (point.ValueKind != JsonValueKind.Object || !point.TryGetProperty("x", out var xValue) ||
                        !point.TryGetProperty("y", out var yValue) || !xValue.TryGetDouble(out var x) || !yValue.TryGetDouble(out var y) ||
                        !double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 600 || y is < 0 or > 160) return false;
                    if (first) path.Append("M "); else { path.Append(" L "); totalDistance += Math.Sqrt(Math.Pow(x - lastX, 2) + Math.Pow(y - lastY, 2)); }
                    path.Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                        .Append(y.ToString("0.##", CultureInfo.InvariantCulture));
                    lastX = x; lastY = y; first = false; totalPoints++;
                    if (totalPoints > 3000) return false;
                }
                paths.Append("<path d=\"").Append(path).Append("\" fill=\"none\" stroke=\"#17212e\" stroke-width=\"3\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
            }
            if (totalDistance < 8) return false;
            svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 600 160\" width=\"600\" height=\"160\">" + paths + "</svg>";
            return true;
        }
        catch (JsonException) { return false; }
    }

    [HttpGet]
    public async Task<IActionResult> SummaryPdfPreview(int roundId, CancellationToken cancellationToken)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var details = await Committee.Load(user.Id, roundId);
        if (details?.Round.TenderReport is not { ReportType: ReportType.ExecutiveSummary, SubmittedAt: not null, Status: not ReportStatus.Draft }) return NotFound();
        try
        {
            var bytes = await GetOrCreateOriginalPdf(details, user.Id, cancellationToken);
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(bytes, "application/pdf");
        }
        catch (InvalidOperationException exception) { return Problem(title: "Original report unavailable", detail: exception.Message, statusCode: 503); }
    }

    [HttpGet]
    public Task<IActionResult> SummaryPdf(int id, CancellationToken cancellationToken) => SummaryPdfDownload(id, cancellationToken);

    private async Task<IActionResult> SummaryPdfDownload(int roundId, CancellationToken cancellationToken)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var details = await Committee.Load(user.Id, roundId);
        if (details?.Round.TenderReport is not { ReportType: ReportType.ExecutiveSummary, SubmittedAt: not null, Status: not ReportStatus.Draft } report) return NotFound();
        try
        {
            var bytes = await GetOrCreateOriginalPdf(details, user.Id, cancellationToken);
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(bytes, "application/pdf", $"Manager-Summary-T{details.Tender.Id}-R{report.Id}-V{report.VersionNumber}.pdf");
        }
        catch (InvalidOperationException exception) { return Problem(title: "Original report unavailable", detail: exception.Message, statusCode: 503); }
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> FinalSignedPdf(int roundId, bool download = true, CancellationToken cancellationToken = default)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        Document? document;
        if (await Users.IsInRoleAsync(user, SystemRoles.TopCommittee))
        {
            var details = await Committee.Load(user.Id, roundId);
            if (details?.Round is not { Status: CommitteeRoundStatus.Approved, TenderReport: not null } || details.SignedCount != 4 ||
                details.ValidVotes.Any(v => v.Decision != VoteDecision.Approve || v.SignatureReportId != details.Round.TenderReportId ||
                    v.SignatureDocumentHash != details.OriginalSummaryPdf?.ContentHash || string.IsNullOrWhiteSpace(v.ElectronicSignatureSvg)) ||
                details.FinalSignedSummaryPdf is not { } memberDocument)
                return NotFound();
            document = memberDocument;
        }
        else if (await Users.IsInRoleAsync(user, SystemRoles.SourcingManager))
        {
            var round = await Db.CommitteeRounds.Include(r => r.TenderRound).ThenInclude(r => r.Tender)
                .Include(r => r.TenderReport).ThenInclude(r => r!.Documents)
                .Include(r => r.Votes).AsSplitQuery().SingleOrDefaultAsync(r => r.Id == roundId &&
                    (r.CommitteeType == CommitteeType.Top || r.CommitteeType == TopCommitteeCompatibility.LegacyTop));
            if (round is not { Status: CommitteeRoundStatus.Approved, TenderReport: not null } ||
                round.TenderReport.Status != ReportStatus.Accepted || round.TenderReport.ReportType != ReportType.ExecutiveSummary ||
                round.TenderReport.CreatedByUserId != user.Id ||
                !await Db.CommitteeMembers.AnyAsync(m => m.TenderId == round.TenderRound.TenderId &&
                    m.CommitteeType == CommitteeType.Evaluation && m.Responsibility == CommitteeResponsibility.SourcingManager && m.UserId == user.Id))
                return NotFound();
            var members = await Db.CommitteeMembers.CountAsync(m => m.TenderId == round.TenderRound.TenderId &&
                (m.CommitteeType == CommitteeType.Top || m.CommitteeType == TopCommitteeCompatibility.LegacyTop));
            var original = round.TenderReport.Documents.SingleOrDefault(d => d.DocumentType == "TopCommitteeOriginalSummaryPdf");
            var signatures = round.Votes.Where(v => v.Decision == VoteDecision.Approve && v.SignatureReportId == round.TenderReportId &&
                v.SignatureDocumentHash != null && !string.IsNullOrWhiteSpace(v.ElectronicSignatureSvg)).ToList();
            if (members != 4 || signatures.Count != 4 || signatures.Select(v => v.CommitteeMemberId).Distinct().Count() != 4 ||
                original?.ContentHash is null || signatures.Any(v => v.SignatureDocumentHash != original.ContentHash)) return NotFound();
            document = round.TenderReport.Documents.SingleOrDefault(d => d.DocumentType == "TopCommitteeFinalSignedPdf");
            if (document is null) return NotFound();
        }
        else return Forbid();
        var bytes = await ReadStoredPdf(document, cancellationToken);
        if (bytes is null || document.ContentHash != Convert.ToHexString(SHA256.HashData(bytes))) return NotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return download ? File(bytes, "application/pdf", document.FileName) : File(bytes, "application/pdf");
    }

    [HttpGet]
    public async Task<IActionResult> SignatureProgress(int roundId)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var round = await Committee.AssignedRounds(user.Id).Include(r => r.TenderRound).Include(r => r.TenderReport)
            .Include(r => r.Votes).ThenInclude(v => v.CommitteeMember).ThenInclude(m => m.User)
            .SingleOrDefaultAsync(r => r.Id == roundId);
        if (round?.TenderReport is not { ReportType: ReportType.ExecutiveSummary } report) return NotFound();
        var members = await Db.CommitteeMembers.Include(m => m.User).Where(m => m.TenderId == round.TenderRound.TenderId &&
            (m.CommitteeType == CommitteeType.Top || m.CommitteeType == TopCommitteeCompatibility.LegacyTop))
            .OrderBy(m => m.AssignedAt).ThenBy(m => m.Id).ToListAsync();
        Response.Headers.CacheControl = "no-store";
        var result = members.Select(member =>
        {
            var signature = round.Votes.SingleOrDefault(v => v.CommitteeMemberId == member.Id &&
                v.Decision == VoteDecision.Approve && v.SignatureReportId == report.Id && !string.IsNullOrWhiteSpace(v.SignatureDocumentHash) &&
                !string.IsNullOrWhiteSpace(v.ElectronicSignatureSvg));
            return new { id = member.Id, name = member.User.FullName, signed = signature is not null,
                signedAt = signature is null ? null : CommitteeDisplay.Date(signature.VotedAt),
                signatureSvgBase64 = string.IsNullOrWhiteSpace(signature?.ElectronicSignatureSvg) ? null :
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(signature.ElectronicSignatureSvg)) };
        }).ToList();
        return Json(new { signedCount = result.Count(m => m.signed), roundStatus = round.Status.ToString(), members = result });
    }

    private string ReportStorageRoot => Path.Combine(environment.ContentRootPath, "Areas", "TopCommittee", "App_Data", "Reports");

    private async Task<byte[]> GetOrCreateOriginalPdf(RoundDetails details, string userId, CancellationToken cancellationToken)
    {
        var report = details.Round.TenderReport!;
        var fileName = $"TopCommittee-Original-R{details.Round.Id}-Report{report.Id}-V{report.VersionNumber}.pdf";
        var htmlFileName = Path.ChangeExtension(fileName, ".html");
        if (details.OriginalSummaryPdf is { } existing)
        {
            var stored = await ReadStoredPdf(existing, cancellationToken);
            if (stored is not null && existing.ContentHash == Convert.ToHexString(SHA256.HashData(stored)) &&
                await ReadOriginalHtml(details, cancellationToken) is not null) return stored;
            if (details.SignedCount > 0)
                throw new InvalidOperationException("The preserved original report file or its source is missing or has failed its integrity check. Existing signatures remain recorded; contact an administrator.");
        }
        var html = await ReportingExport.HtmlView(this, views, details,
            "/Areas/TopCommittee/Views/Decisions/SummaryPdf.cshtml");
        var pdf = await BrowserReportPdf.Create(html, environment.ContentRootPath, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(pdf));
        var root = Path.GetFullPath(ReportStorageRoot);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, fileName);
        var htmlPath = Path.Combine(root, htmlFileName);
        var resource = $"Orange:TopCommittee:Tender:{details.Tender.Id}";
        await using var transaction = await Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await Db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000; IF @result < 0 THROW 51000, 'Committee operation is busy. Retry.', 1;", cancellationToken);
        Db.ChangeTracker.Clear();
        var tracked = await Db.Documents.SingleOrDefaultAsync(d => d.TenderReportId == report.Id && d.DocumentType == "TopCommitteeOriginalSummaryPdf", cancellationToken);
        if (tracked is not null)
        {
            var oldBytes = await ReadStoredPdf(tracked, cancellationToken);
            if (oldBytes is not null && tracked.ContentHash == Convert.ToHexString(SHA256.HashData(oldBytes)))
            {
                if (System.IO.File.Exists(htmlPath))
                {
                    var oldHtml = await ReadOriginalHtml(details, cancellationToken);
                    if (oldHtml is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return oldBytes;
                    }
                }
            }
            if (await Db.CommitteeVotes.AnyAsync(v => v.SignatureReportId == report.Id, cancellationToken))
                throw new InvalidOperationException("The original report cannot be replaced after signatures have been recorded.");
            tracked.FileName = fileName;
            tracked.StoragePath = fileName;
            tracked.ContentType = "application/pdf";
            tracked.SizeBytes = pdf.LongLength;
            tracked.ContentHash = hash;
            tracked.UploadedByUserId = details.Manager?.Id ?? userId;
            tracked.UploadedAt = DateTime.UtcNow;
        }
        else
        {
            Db.Documents.Add(new Document
            {
                FileName = fileName, StoragePath = fileName, ContentType = "application/pdf", SizeBytes = pdf.LongLength,
                ContentHash = hash, DocumentType = "TopCommitteeOriginalSummaryPdf", UploadedByUserId = details.Manager?.Id ?? userId,
                UploadedAt = DateTime.UtcNow, TenderReportId = report.Id
            });
        }
        await System.IO.File.WriteAllBytesAsync(path, pdf, cancellationToken);
        await System.IO.File.WriteAllTextAsync(htmlPath, html, cancellationToken);
        Db.AuditLogs.Add(new AuditLog
        {
            UserId = userId, Action = "TopCommitteeOriginalPdfPreserved", EntityName = "TenderReport", EntityId = report.Id.ToString(),
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { report.Id, report.VersionNumber, hash, SourceHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(html))), fileName }), CreatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return pdf;
    }

    private async Task<string?> ReadOriginalHtml(RoundDetails details, CancellationToken cancellationToken)
    {
        var report = details.Round.TenderReport;
        if (report is null) return null;
        var pdfFileName = $"TopCommittee-Original-R{details.Round.Id}-Report{report.Id}-V{report.VersionNumber}.pdf";
        var root = Path.GetFullPath(ReportStorageRoot);
        var path = Path.GetFullPath(Path.Combine(root, Path.ChangeExtension(pdfFileName, ".html")));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path)) return null;
        var html = await System.IO.File.ReadAllTextAsync(path, cancellationToken);
        var audit = await Db.AuditLogs.AsNoTracking().Where(a => a.Action == "TopCommitteeOriginalPdfPreserved" &&
            a.EntityName == "TenderReport" && a.EntityId == report.Id.ToString())
            .OrderByDescending(a => a.CreatedAt).Select(a => a.NewValues).FirstOrDefaultAsync(cancellationToken);
        if (audit is null) return null;
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(audit);
            var expected = json.RootElement.GetProperty("SourceHash").GetString();
            var actual = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(html)));
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) ? html : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    private async Task<byte[]?> ReadStoredPdf(Document document, CancellationToken cancellationToken)
    {
        if (Path.GetFileName(document.StoragePath) != document.StoragePath ||
            document.DocumentType is not ("TopCommitteeOriginalSummaryPdf" or "TopCommitteeFinalSignedPdf")) return null;
        var root = Path.GetFullPath(ReportStorageRoot);
        var path = Path.GetFullPath(Path.Combine(root, document.StoragePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path)) return null;
        var bytes = await System.IO.File.ReadAllBytesAsync(path, cancellationToken);
        return bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8) ? bytes : null;
    }

    public async Task<IActionResult> History(int? id, string? search, CommitteeRoundStatus? status, int? tenderId, int page = 1)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        if (status.HasValue && !Enum.IsDefined(status.Value)) return BadRequest();
        Response.Headers.CacheControl = "no-store";
        search = search?.Trim();
        var query = TopCommitteeSearch.Apply(Committee.RoundList(user.Id).AsNoTracking(), search, status);
        if (tenderId.HasValue) query = query.Where(r => r.TenderRound.TenderId == tenderId);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20m)));
        var selected = id.HasValue ? await Committee.Load(user.Id, id.Value) : null;
        if (id.HasValue && selected is null) return NotFound();
        var model = new DecisionHistory
        {
            Search = search, Status = status, TenderId = tenderId, Page = page, Total = total, Selected = selected,
            Rounds = await query.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
                .Skip((page - 1) * 20).Take(20).AsSplitQuery().ToListAsync()
        };
        return Request.Headers["X-TopCommittee-Live"] == "1" ? PartialView("_HistoryResults", model) : View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Download(int id, int roundId)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var details = await Committee.Load(user.Id, roundId);
        var document = details?.Documents.SingleOrDefault(d => d.Id == id);
        if (document is null || document.DocumentType != "DepartmentSupportingDocument" ||
            Path.GetFileName(document.StoragePath) != document.StoragePath) return NotFound();
        // This is the sole implemented storage convention in the current project. Other handlers are a team integration.
        var path = Path.Combine(environment.ContentRootPath, "Areas", "Department", "App_Data", "Uploads", document.StoragePath);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "application/octet-stream", document.FileName);
    }
}
